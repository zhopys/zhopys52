using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;

namespace MiniFinance.Services;

public class ReportExportService : IReportExportService
{
    private readonly ApplicationDbContext _db;
    private readonly IReportPdfService _pdfService;

    public ReportExportService(ApplicationDbContext db, IReportPdfService pdfService)
    {
        _db = db;
        _pdfService = pdfService;
    }

    public (string ContentType, string FileName, byte[] Data) Export(ReportAnalyticsSnapshot snapshot, string format, string userId)
    {
        var f = snapshot.Filters;
        var report = f.ExportReport.ToLowerInvariant();
        var stem = report is "full" or ""
            ? $"MiniFinance_{f.Start:yyyyMMdd}_{f.End:yyyyMMdd}"
            : $"MiniFinance_{report}_{f.Start:yyyyMMdd}_{f.End:yyyyMMdd}";
        return format.ToLowerInvariant() switch
        {
            "csv" => ("text/csv; charset=utf-8", $"{stem}_transactions.csv", ExportCsv(snapshot, userId)),
            "xlsx" or "excel" => ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{stem}.xlsx", ExportXlsx(snapshot, userId)),
            "pdf" => ("application/pdf", $"{stem}.pdf", ExportPdf(snapshot, userId)),
            _ => throw new ArgumentException($"Unknown format: {format}")
        };
    }

    public byte[] ExportCsv(ReportAnalyticsSnapshot snapshot, string userId)
    {
        var f = snapshot.Filters;
        var sb = new StringBuilder();
        sb.AppendLine($"# MiniFinance export");
        sb.AppendLine($"# Period: {f.Start:yyyy-MM-dd} — {f.End:yyyy-MM-dd}");
        sb.AppendLine($"# Compare: {f.CompareMode}");
        if (f.ProjectId.HasValue)
            sb.AppendLine($"# ProjectId: {f.ProjectId}");
        if (f.Categories.Count > 0)
            sb.AppendLine($"# Categories: {string.Join(", ", f.Categories)}");
        sb.AppendLine($"# Generated: {snapshot.GeneratedAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("Date,Amount,Description,Category,Project,Counterparty,PaymentMethod");

        var tx = LoadTransactions(userId, f);
        foreach (var t in tx)
        {
            sb.AppendLine(string.Join(",",
                t.Date.ToString("yyyy-MM-dd"),
                t.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Escape(t.Description),
                Escape(t.Category),
                Escape(t.Project?.Name),
                Escape(t.Counterparty),
                Escape(t.PaymentMethod?.ToString())));
        }

        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = Encoding.UTF8.GetBytes(sb.ToString());
        var bytes = new byte[bom.Length + content.Length];
        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        Buffer.BlockCopy(content, 0, bytes, bom.Length, content.Length);
        return bytes;
    }

    public byte[] ExportXlsx(ReportAnalyticsSnapshot snapshot, string userId)
    {
        var report = snapshot.Filters.ExportReport.ToLowerInvariant();
        using var wb = new XLWorkbook();

        if (report is "full" or "summary")
        {
            BuildSummarySheet(wb, snapshot);
            BuildPlSheet(wb, snapshot);
            BuildCashFlowSheet(wb, snapshot);
            BuildTrialBalanceSheet(wb, snapshot);
            BuildIncomeBookSheet(wb, snapshot);
            BuildProfitabilitySheet(wb, snapshot);
            BuildForecastSheet(wb, snapshot);
            BuildTransactionsSheet(wb, snapshot, userId);
        }
        else if (report == "pl")
            BuildPlSheet(wb, snapshot);
        else if (report == "cashflow")
            BuildCashFlowSheet(wb, snapshot);
        else if (report == "transactions")
            BuildTransactionsSheet(wb, snapshot, userId);
        else if (report is "profitability")
            BuildProfitabilitySheet(wb, snapshot);
        else if (report is "trial" or "categories")
            BuildTrialBalanceSheet(wb, snapshot);
        else if (report is "book")
            BuildIncomeBookSheet(wb, snapshot);
        else if (report is "forecast")
            BuildForecastSheet(wb, snapshot);
        else
            BuildSummarySheet(wb, snapshot);

        if (wb.Worksheets.Count == 0)
            BuildSummarySheet(wb, snapshot);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ExportPdf(ReportAnalyticsSnapshot snapshot, string userId)
    {
        var report = snapshot.Filters.ExportReport.ToLowerInvariant();
        var f = snapshot.Filters;
        var tx = MapTransactions(LoadTransactions(userId, f));

        return report switch
        {
            "pl" when snapshot.ProfitLoss != null =>
                _pdfService.GenerateProfitLossPdf(snapshot.ProfitLoss, f.Start, f.End),
            "cashflow" when snapshot.CashFlow != null =>
                _pdfService.GenerateCashFlowPdf(snapshot.CashFlow, f.Start, f.End),
            "trial" or "categories" when snapshot.TrialBalance != null =>
                _pdfService.GenerateTrialBalancePdf(snapshot.TrialBalance, f.Start, f.End),
            "book" =>
                _pdfService.GenerateIncomeBookPdf(snapshot.IncomeExpenseBook, f.Start, f.End),
            "transactions" =>
                _pdfService.GenerateTransactionsPdf(tx, f.Start, f.End),
            "profitability" =>
                _pdfService.GenerateProfitabilityPdf(snapshot.ProfitabilityMatrix, f.Start, f.End),
            "forecast" =>
                _pdfService.GenerateForecastPdf(snapshot.Forecast, f.Start, f.End),
            _ => _pdfService.GenerateFullReportPdf(snapshot, tx)
        };
    }

    private static List<TransactionPdfRow> MapTransactions(List<Data.Models.Transaction> tx) =>
        tx.Select(t => new TransactionPdfRow(
            t.Date,
            string.IsNullOrWhiteSpace(t.Description) ? t.Category : t.Description.Trim(),
            string.IsNullOrWhiteSpace(t.Category) ? "Без категории" : t.Category,
            t.Amount,
            t.Project?.Name,
            string.IsNullOrWhiteSpace(t.Counterparty) ? null : t.Counterparty.Trim())).ToList();

    private List<Data.Models.Transaction> LoadTransactions(string userId, ReportFilters f)
    {
        var q = _db.Transactions.Include(t => t.Project)
            .Where(t => t.UserId == userId && t.Date >= f.Start && t.Date <= f.End);

        if (f.ProjectId.HasValue)
            q = q.Where(t => t.ProjectId == f.ProjectId);

        var list = q.OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();

        if (f.Categories.Count > 0)
        {
            var set = new HashSet<string>(f.Categories, StringComparer.OrdinalIgnoreCase);
            list = list.Where(t => set.Contains(t.Category)).ToList();
        }

        return list;
    }

    private static void BuildSummarySheet(XLWorkbook wb, ReportAnalyticsSnapshot s)
    {
        var ws = wb.Worksheets.Add("Сводка");
        ws.Cell(1, 1).Value = "MiniFinance — Финансовый отчёт";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(2, 1).Value = $"Период: {s.Filters.Start:dd.MM.yyyy} — {s.Filters.End:dd.MM.yyyy}";
        ws.Cell(3, 1).Value = $"Сформировано: {s.GeneratedAt.ToLocalTime():dd.MM.yyyy HH:mm}";
        if (s.Filters.ProjectId.HasValue)
            ws.Cell(4, 1).Value = $"Проект ID: {s.Filters.ProjectId}";

        ws.Cell(6, 1).Value = "KPI";
        ws.Cell(6, 1).Style.Font.Bold = true;
        ws.Cell(7, 1).Value = "Показатель";
        ws.Cell(7, 2).Value = "Значение";
        ws.Cell(7, 3).Value = "Пред. период";
        ws.Cell(7, 4).Value = "Изменение %";
        ws.Range(7, 1, 7, 4).Style.Font.Bold = true;

        int r = 8;
        foreach (var k in s.Kpi)
        {
            ws.Cell(r, 1).Value = k.Label;
            ws.Cell(r, 2).Value = (double)k.Value;
            ws.Cell(r, 3).Value = (double)k.PreviousValue;
            ws.Cell(r, 4).Value = (double)k.ChangePercent;
            if (k.Format != "percent")
            {
                ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0";
                ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0";
            }
            ws.Cell(r, 4).Style.NumberFormat.Format = "0.0\"%\"";
            r++;
        }

        ws.Cell(r + 1, 1).Value = "Налоговый резерв";
        ws.Cell(r + 1, 2).Value = (double)s.TaxReserve;
        ws.Cell(r + 2, 1).Value = "Свободные деньги";
        ws.Cell(r + 2, 2).Value = (double)s.FreeCash;
        ws.Columns().AdjustToContents();
    }

    private static void BuildPlSheet(XLWorkbook wb, ReportAnalyticsSnapshot s)
    {
        var pl = s.ProfitLoss;
        if (pl == null) return;

        var ws = wb.Worksheets.Add("P&L");
        ws.Cell(1, 1).Value = "Отчёт о прибылях и убытках";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = $"{s.Filters.Start:dd.MM.yyyy} — {s.Filters.End:dd.MM.yyyy}";

        ws.Cell(4, 1).Value = "Показатель";
        ws.Cell(4, 2).Value = "Сумма (Br)";
        ws.Range(4, 1, 4, 2).Style.Font.Bold = true;

        ws.Cell(5, 1).Value = "Доходы";
        ws.Cell(5, 2).Value = (double)pl.TotalIncome;
        ws.Row(5).OutlineLevel = 0;

        int incomeStart = 6;
        foreach (var c in pl.IncomeByCategory)
        {
            ws.Cell(incomeStart, 1).Value = "  " + c.Category;
            ws.Cell(incomeStart, 2).Value = (double)c.Amount;
            ws.Row(incomeStart).OutlineLevel = 1;
            incomeStart++;
        }
        if (incomeStart > 6)
        {
            ws.Cell(incomeStart, 1).Value = "Итого доходы";
            ws.Cell(incomeStart, 2).FormulaA1 = $"=SUM(B6:B{incomeStart - 1})";
            ws.Row(incomeStart).Style.Font.Bold = true;
            incomeStart++;
        }

        int expHeader = incomeStart + 1;
        ws.Cell(expHeader, 1).Value = "Расходы";
        ws.Cell(expHeader, 2).Value = (double)pl.TotalExpense;
        ws.Row(expHeader).Style.Font.Bold = true;

        int r = expHeader + 1;
        int firstCat = r;
        foreach (var c in pl.ExpenseByCategory)
        {
            ws.Cell(r, 1).Value = "  " + c.Category;
            ws.Cell(r, 2).Value = (double)c.Amount;
            ws.Row(r).OutlineLevel = 1;
            r++;
        }
        if (r > firstCat)
        {
            ws.Cell(r, 1).Value = "Итого расходы";
            ws.Cell(r, 2).FormulaA1 = $"=SUM(B{firstCat}:B{r - 1})";
            ws.Row(r).Style.Font.Bold = true;
            r++;
        }

        ws.Cell(r, 1).Value = "Чистая прибыль";
        ws.Cell(r, 2).FormulaA1 = "=B5+B" + expHeader;
        ws.Row(r).Style.Font.Bold = true;

        ws.Column(2).Style.NumberFormat.Format = "#,##0.00";
        ws.Outline.SummaryVLocation = XLOutlineSummaryVLocation.Top;
        ws.Columns().AdjustToContents();
    }

    private static void BuildCashFlowSheet(XLWorkbook wb, ReportAnalyticsSnapshot s)
    {
        var cf = s.CashFlow;
        if (cf == null) return;

        var ws = wb.Worksheets.Add("Cash Flow");
        ws.Cell(1, 1).Value = "Движение денежных средств";
        ws.Cell(1, 1).Style.Font.Bold = true;

        ws.Cell(3, 1).Value = "Вид деятельности";
        ws.Cell(3, 2).Value = "Сумма";
        ws.Cell(4, 1).Value = "Операционная";
        ws.Cell(4, 2).Value = (double)cf.OperatingCashFlow;
        ws.Cell(5, 1).Value = "Инвестиционная";
        ws.Cell(5, 2).Value = (double)cf.InvestmentCashFlow;
        ws.Cell(6, 1).Value = "Финансовая";
        ws.Cell(6, 2).Value = (double)cf.FinancingCashFlow;
        ws.Cell(7, 1).Value = "Чистый поток";
        ws.Cell(7, 2).FormulaA1 = "=SUM(B4:B6)";
        ws.Cell(7, 1).Style.Font.Bold = true;

        int r = 9;
        ws.Cell(r, 1).Value = "Категория";
        ws.Cell(r, 2).Value = "Приход";
        ws.Cell(r, 3).Value = "Расход";
        ws.Cell(r, 4).Value = "Нетто";
        ws.Range(r, 1, r, 4).Style.Font.Bold = true;
        r++;
        int start = r;
        foreach (var c in cf.CategoryDetails)
        {
            ws.Cell(r, 1).Value = c.Category;
            ws.Cell(r, 2).Value = (double)c.Income;
            ws.Cell(r, 3).Value = (double)c.Expense;
            ws.Cell(r, 4).FormulaA1 = $"=B{r}-C{r}";
            ws.Row(r).OutlineLevel = 1;
            r++;
        }
        if (r > start)
        {
            ws.Cell(r, 1).Value = "Итого";
            ws.Cell(r, 2).FormulaA1 = $"=SUM(B{start}:B{r - 1})";
            ws.Cell(r, 3).FormulaA1 = $"=SUM(C{start}:C{r - 1})";
            ws.Cell(r, 4).FormulaA1 = $"=SUM(D{start}:D{r - 1})";
            ws.Row(r).Style.Font.Bold = true;
        }
        ws.Outline.SummaryVLocation = XLOutlineSummaryVLocation.Top;
        ws.Columns().AdjustToContents();
    }

    private static void BuildProfitabilitySheet(XLWorkbook wb, ReportAnalyticsSnapshot s)
    {
        var ws = wb.Worksheets.Add("Рентабельность");
        ws.Cell(1, 1).Value = "Проект / отдел";
        ws.Cell(1, 2).Value = "Выручка";
        ws.Cell(1, 3).Value = "Маржа %";
        ws.Cell(1, 4).Value = "Зарплаты";
        ws.Cell(1, 5).Value = "Аренда";
        ws.Cell(1, 6).Value = "Налоги";
        ws.Cell(1, 7).Value = "Чистый доход";
        ws.Range(1, 1, 1, 7).Style.Font.Bold = true;

        int r = 2;
        int dataStart = r;
        foreach (var row in s.ProfitabilityMatrix)
        {
            ws.Cell(r, 1).Value = row.Name;
            ws.Cell(r, 2).Value = (double)row.Revenue;
            ws.Cell(r, 3).Value = (double)row.MarginPercent;
            ws.Cell(r, 4).Value = (double)row.Payroll;
            ws.Cell(r, 5).Value = (double)row.Rent;
            ws.Cell(r, 6).Value = (double)row.Taxes;
            ws.Cell(r, 7).Value = (double)row.NetIncome;
            r++;
        }
        if (r > dataStart)
        {
            ws.Cell(r, 1).Value = "Итого";
            ws.Cell(r, 2).FormulaA1 = $"=SUM(B{dataStart}:B{r - 1})";
            ws.Cell(r, 4).FormulaA1 = $"=SUM(D{dataStart}:D{r - 1})";
            ws.Cell(r, 5).FormulaA1 = $"=SUM(E{dataStart}:E{r - 1})";
            ws.Cell(r, 6).FormulaA1 = $"=SUM(F{dataStart}:F{r - 1})";
            ws.Cell(r, 7).FormulaA1 = $"=SUM(G{dataStart}:G{r - 1})";
            ws.Row(r).Style.Font.Bold = true;
        }
        ws.Columns().AdjustToContents();
    }

    private static void BuildForecastSheet(XLWorkbook wb, ReportAnalyticsSnapshot s)
    {
        var fc = s.Forecast;
        var ws = wb.Worksheets.Add("Прогноз");
        ws.Cell(1, 1).Value = $"Прогноз баланса ({fc.HorizonDays} дн.)";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = $"Текущий остаток: {fc.CurrentBalance:N0} Br";
        ws.Cell(3, 1).Value = fc.HasRisk ? $"⚠ Риск: мин. {fc.MinBalance:N0} Br" : "Риск не выявлен";

        ws.Cell(5, 1).Value = "Дата";
        ws.Cell(5, 2).Value = "Баланс";
        ws.Cell(5, 3).Value = "Зона риска";
        ws.Range(5, 1, 5, 3).Style.Font.Bold = true;

        int r = 6;
        foreach (var p in fc.Points)
        {
            ws.Cell(r, 1).Value = p.Date;
            ws.Cell(r, 2).Value = (double)p.Balance;
            ws.Cell(r, 3).Value = p.IsGap ? "Да" : "";
            if (p.IsGap) ws.Row(r).Style.Fill.BackgroundColor = XLColor.LightPink;
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    private static void BuildTrialBalanceSheet(XLWorkbook wb, ReportAnalyticsSnapshot s)
    {
        var tb = s.TrialBalance;
        if (tb == null) return;

        var ws = wb.Worksheets.Add("ОСВ");
        ws.Cell(1, 1).Value = "Оборотно-сальдовая ведомость";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = $"{s.Filters.Start:dd.MM.yyyy} — {s.Filters.End:dd.MM.yyyy}";

        ws.Cell(4, 1).Value = "Категория";
        ws.Cell(4, 2).Value = "Сальдо нач.";
        ws.Cell(4, 3).Value = "Дебет";
        ws.Cell(4, 4).Value = "Кредит";
        ws.Cell(4, 5).Value = "Сальдо кон.";
        ws.Range(4, 1, 4, 5).Style.Font.Bold = true;

        int r = 5;
        foreach (var e in tb.Entries)
        {
            ws.Cell(r, 1).Value = e.Category;
            ws.Cell(r, 2).Value = (double)e.OpeningBalance;
            ws.Cell(r, 3).Value = (double)e.Debit;
            ws.Cell(r, 4).Value = (double)e.Credit;
            ws.Cell(r, 5).Value = (double)e.ClosingBalance;
            r++;
        }
        ws.Cell(r, 1).Value = "Итого";
        ws.Cell(r, 2).Value = (double)tb.TotalOpeningBalance;
        ws.Cell(r, 3).Value = (double)tb.TotalDebit;
        ws.Cell(r, 4).Value = (double)tb.TotalCredit;
        ws.Cell(r, 5).Value = (double)tb.TotalClosingBalance;
        ws.Row(r).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
    }

    private static void BuildIncomeBookSheet(XLWorkbook wb, ReportAnalyticsSnapshot s)
    {
        var ws = wb.Worksheets.Add("Книга учёта");
        ws.Cell(1, 1).Value = "Книга учёта доходов и расходов";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = $"{s.Filters.Start:dd.MM.yyyy} — {s.Filters.End:dd.MM.yyyy}";

        ws.Cell(4, 1).Value = "№";
        ws.Cell(4, 2).Value = "Дата";
        ws.Cell(4, 3).Value = "Документ";
        ws.Cell(4, 4).Value = "Контрагент";
        ws.Cell(4, 5).Value = "Содержание";
        ws.Cell(4, 6).Value = "Доход";
        ws.Cell(4, 7).Value = "Расход";
        ws.Range(4, 1, 4, 7).Style.Font.Bold = true;

        int r = 5;
        foreach (var e in s.IncomeExpenseBook)
        {
            ws.Cell(r, 1).Value = e.EntryNumber;
            ws.Cell(r, 2).Value = e.Date;
            ws.Cell(r, 3).Value = e.DocumentNumber;
            ws.Cell(r, 4).Value = e.Counterparty;
            ws.Cell(r, 5).Value = e.Description;
            ws.Cell(r, 6).Value = (double)e.Income;
            ws.Cell(r, 7).Value = (double)e.Expense;
            r++;
        }
        if (r > 5)
        {
            ws.Cell(r, 5).Value = "Итого";
            ws.Cell(r, 6).FormulaA1 = $"=SUM(F5:F{r - 1})";
            ws.Cell(r, 7).FormulaA1 = $"=SUM(G5:G{r - 1})";
            ws.Row(r).Style.Font.Bold = true;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildTransactionsSheet(XLWorkbook wb, ReportAnalyticsSnapshot s, string userId)
    {
        var ws = wb.Worksheets.Add("Транзакции");
        ws.Cell(1, 1).Value = "Дата";
        ws.Cell(1, 2).Value = "Описание";
        ws.Cell(1, 3).Value = "Категория";
        ws.Cell(1, 4).Value = "Сумма";
        ws.Cell(1, 5).Value = "Проект";
        ws.Cell(1, 6).Value = "Контрагент";
        ws.Range(1, 1, 1, 6).Style.Font.Bold = true;

        var tx = LoadTransactions(userId, s.Filters);
        int r = 2;
        int start = r;
        foreach (var t in tx)
        {
            ws.Cell(r, 1).Value = t.Date;
            ws.Cell(r, 2).Value = t.Description;
            ws.Cell(r, 3).Value = t.Category;
            ws.Cell(r, 4).Value = (double)t.Amount;
            ws.Cell(r, 5).Value = t.Project?.Name ?? "";
            ws.Cell(r, 6).Value = t.Counterparty ?? "";
            r++;
        }
        if (r > start)
        {
            ws.Cell(r, 1).Value = "Итого";
            ws.Cell(r, 4).FormulaA1 = $"=SUM(D{start}:D{r - 1})";
            ws.Row(r).Style.Font.Bold = true;
        }
        ws.Columns().AdjustToContents();
    }

    private static string Escape(string? s) => '"' + (s ?? "").Replace("\"", "\"\"") + '"';
}

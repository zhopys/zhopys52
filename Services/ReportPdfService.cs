using System.Globalization;
using MiniFinance.Data.Models;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Layout.Borders;

namespace MiniFinance.Services;

public class ReportPdfService : IReportPdfService
{
    private static readonly DeviceRgb Accent = new(0, 212, 170);
    private static readonly DeviceRgb HeaderBg = new(240, 253, 250);
    private static readonly DeviceRgb Danger = new(239, 68, 68);

    private readonly PdfFont _fontRegular;
    private readonly PdfFont _fontBold;
    private readonly PdfFont _fontByn;

    public ReportPdfService()
    {
        _fontRegular = LoadFont(bold: false);
        _fontBold = LoadFont(bold: true);
        _fontByn = LoadBynFont();
    }

    public byte[] GenerateProfitLossPdf(ProfitLossReport report, DateTime start, DateTime end) =>
        BuildPdf("Отчёт о прибылях и убытках (P&L)", start, end, body =>
        {
            body.Add(Line($"Доходы: {report.TotalIncome:N0}{BynCurrency.Suffix}"));
            body.Add(Line($"Расходы: {Math.Abs(report.TotalExpense):N0}{BynCurrency.Suffix}"));
            body.Add(Line($"Чистая прибыль: {report.NetProfit:N0}{BynCurrency.Suffix} ({report.ProfitMargin:F1}%)", bold: true));
            body.Add(Spacer());

            if (report.ExpenseByCategory.Any())
            {
                body.Add(SectionTitle("Расходы по категориям"));
                body.Add(BuildCategoryTable(report.ExpenseByCategory.Select(c => (c.Category, c.Amount, c.Percentage))));
            }
            if (report.IncomeByCategory.Any())
            {
                body.Add(SectionTitle("Доходы по категориям").SetMarginTop(12));
                body.Add(BuildCategoryTable(report.IncomeByCategory.Select(c => (c.Category, c.Amount, c.Percentage))));
            }
        });

    public byte[] GenerateCashFlowPdf(CashFlowStatementReport report, DateTime start, DateTime end) =>
        BuildPdf("Отчёт о движении денежных средств", start, end, body =>
        {
            body.Add(Line($"Операционная: {report.OperatingCashFlow:N0}{BynCurrency.Suffix}"));
            body.Add(Line($"Инвестиционная: {report.InvestmentCashFlow:N0}{BynCurrency.Suffix}"));
            body.Add(Line($"Финансовая: {report.FinancingCashFlow:N0}{BynCurrency.Suffix}"));
            body.Add(Line($"Чистый поток: {report.NetCashFlow:N0}{BynCurrency.Suffix}", bold: true));
            body.Add(Spacer());

            if (report.CategoryDetails.Any())
            {
                var table = new Table(UnitValue.CreatePercentArray(new float[] { 40, 20, 20, 20 })).UseAllAvailableWidth();
                table.AddHeaderCell(H("Категория"));
                table.AddHeaderCell(H("Приход"));
                table.AddHeaderCell(H("Расход"));
                table.AddHeaderCell(H("Нетто"));
                foreach (var c in report.CategoryDetails)
                {
                    table.AddCell(D(c.Category));
                    table.AddCell(N(c.Income));
                    table.AddCell(N(c.Expense));
                    table.AddCell(N(c.NetFlow));
                }
                body.Add(table);
            }
        });

    public byte[] GenerateTrialBalancePdf(TrialBalanceReport report, DateTime start, DateTime end) =>
        BuildPdf("Оборотно-сальдовая ведомость (ОСВ)", start, end, body =>
        {
            body.Add(BuildTrialBalanceTable(report));
            body.Add(Spacer(8));
            body.Add(Line($"Итого сальдо на начало: {report.TotalOpeningBalance:N0}{BynCurrency.Suffix}"));
            body.Add(Line($"Итого дебет: {report.TotalDebit:N0}{BynCurrency.Suffix}  |  кредит: {report.TotalCredit:N0}{BynCurrency.Suffix}"));
            body.Add(Line($"Итого сальдо на конец: {report.TotalClosingBalance:N0}{BynCurrency.Suffix}", bold: true));
        });

    public byte[] GenerateIncomeBookPdf(IReadOnlyList<IncomeExpenseBookEntry> entries, DateTime start, DateTime end) =>
        BuildPdf("Книга учёта доходов и расходов", start, end, body =>
        {
            var totalIncome = entries.Sum(e => e.Income);
            var totalExpense = entries.Sum(e => e.Expense);
            body.Add(Line($"Записей: {entries.Count}  |  доходы: {totalIncome:N0}{BynCurrency.Suffix}  |  расходы: {totalExpense:N0}{BynCurrency.Suffix}"));
            body.Add(Spacer(6));
            body.Add(BuildIncomeBookTable(entries));
        });

    public byte[] GenerateProfitabilityPdf(IReadOnlyList<ProfitabilityMatrixRowDto> rows, DateTime start, DateTime end) =>
        BuildPdf("Рентабельность проектов и отделов", start, end, body =>
        {
            if (rows.Count == 0)
                body.Add(Line("Нет данных за выбранный период."));
            else
                body.Add(BuildProfitabilityTable(rows));
        });

    public byte[] GenerateForecastPdf(CashForecastChartDto forecast, DateTime start, DateTime end) =>
        BuildPdf($"Прогноз ликвидности ({forecast.HorizonDays} дн.)", start, end, body =>
        {
            body.Add(Line($"Текущий остаток: {forecast.CurrentBalance:N0}{BynCurrency.Suffix}"));
            if (forecast.HasRisk)
                body.Add(Line($"Риск кассового разрыва — мин. остаток: {forecast.MinBalance:N0}{BynCurrency.Suffix}", color: Danger));
            else
                body.Add(Line("Критических зон не выявлено"));
            body.Add(Spacer(8));
            body.Add(BuildForecastTable(forecast));
        });

    public byte[] GenerateTransactionsPdf(IReadOnlyList<TransactionPdfRow> rows, DateTime start, DateTime end) =>
        BuildPdf("Реестр транзакций", start, end, body =>
        {
            var total = rows.Sum(r => r.Amount);
            body.Add(Line($"Операций: {rows.Count}  |  итого по суммам: {total:N2}{BynCurrency.Suffix}", bold: true));
            body.Add(Spacer(6));
            body.Add(BuildTransactionsTable(rows));
        });

    public byte[] GenerateFullReportPdf(ReportAnalyticsSnapshot snapshot, IReadOnlyList<TransactionPdfRow> transactions)
    {
        var f = snapshot.Filters;
        using var ms = new MemoryStream();
        using var writer = CreateWriter(ms);
        using var pdf = new PdfDocument(writer);
        using var doc = new Document(pdf);
        doc.SetMargins(48, 48, 72, 48);

        doc.Add(new Paragraph("MiniFinance").SetFont(_fontBold).SetFontSize(22).SetFontColor(Accent));
        doc.Add(SectionTitle("Полный финансовый отчёт").SetFontSize(18));
        doc.Add(Line($"Период: {f.Start:dd.MM.yyyy} — {f.End:dd.MM.yyyy}", size: 10, color: ColorConstants.GRAY));
        doc.Add(Line($"Сформировано: {DateTime.Now:dd.MM.yyyy HH:mm}", size: 9, color: ColorConstants.GRAY));
        if (f.ProjectId.HasValue)
            doc.Add(Line($"Проект ID: {f.ProjectId}", size: 9, color: ColorConstants.DARK_GRAY));
        if (!string.IsNullOrEmpty(snapshot.SelectedCategory))
            doc.Add(Line($"Категория: {snapshot.SelectedCategory}", size: 9, color: ColorConstants.DARK_GRAY));
        doc.Add(Spacer(12));

        doc.Add(SectionTitle("Ключевые показатели"));
        doc.Add(BuildKpiTable(snapshot.Kpi, f.CompareMode));
        doc.Add(Line($"Налоговый резерв: {snapshot.TaxReserve:N0}{BynCurrency.Suffix}  |  Свободные деньги: {snapshot.FreeCash:N0}{BynCurrency.Suffix}")
            .SetMarginTop(10));

        if (snapshot.ProfitLoss != null)
        {
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            AddProfitLossSection(doc, snapshot.ProfitLoss);
        }

        if (snapshot.CashFlow != null)
        {
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            AddCashFlowSection(doc, snapshot.CashFlow);
        }

        if (snapshot.TrialBalance?.Entries.Count > 0)
        {
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            doc.Add(SectionTitle("Оборотно-сальдовая ведомость"));
            doc.Add(BuildTrialBalanceTable(snapshot.TrialBalance));
        }

        if (snapshot.IncomeExpenseBook.Count > 0)
        {
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            doc.Add(SectionTitle("Книга учёта доходов и расходов"));
            var inc = snapshot.IncomeExpenseBook.Sum(e => e.Income);
            var exp = snapshot.IncomeExpenseBook.Sum(e => e.Expense);
            doc.Add(Line($"Записей: {snapshot.IncomeExpenseBook.Count}  |  доходы: {inc:N0}{BynCurrency.Suffix}  |  расходы: {exp:N0}{BynCurrency.Suffix}"));
            doc.Add(BuildIncomeBookTable(snapshot.IncomeExpenseBook).SetMarginTop(8));
        }

        if (snapshot.ProfitabilityMatrix.Any())
        {
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            doc.Add(SectionTitle("Рентабельность проектов"));
            doc.Add(BuildProfitabilityTable(snapshot.ProfitabilityMatrix));
        }

        if (snapshot.Forecast.Points.Any())
        {
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            doc.Add(SectionTitle($"Прогноз ликвидности ({snapshot.Forecast.HorizonDays} дн.)"));
            doc.Add(Line($"Текущий остаток: {snapshot.Forecast.CurrentBalance:N0}{BynCurrency.Suffix}"));
            if (snapshot.Forecast.HasRisk)
                doc.Add(Line($"Риск кассового разрыва — мин. остаток: {snapshot.Forecast.MinBalance:N0}{BynCurrency.Suffix}", color: Danger));
            doc.Add(BuildForecastTable(snapshot.Forecast).SetMarginTop(8));
        }

        if (snapshot.CriticalReminders.Any())
        {
            doc.Add(Spacer());
            doc.Add(SectionTitle("Налоговый календарь").SetFontSize(12));
            foreach (var a in snapshot.CriticalReminders.Take(8))
                doc.Add(Line($"• {a.Title} — {a.DueDate:dd.MM.yyyy}: {a.Amount:N0}{BynCurrency.Suffix} ({a.Severity})"));
        }

        if (transactions.Count > 0)
        {
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            doc.Add(SectionTitle("Все транзакции за период"));
            var total = transactions.Sum(t => t.Amount);
            doc.Add(Line($"Операций: {transactions.Count}  |  итого: {total:N2}{BynCurrency.Suffix}", bold: true));
            doc.Add(BuildTransactionsTable(transactions).SetMarginTop(8));
        }

        doc.Add(Spacer(16));
        doc.Add(FooterParagraph());
        doc.Close();
        return ms.ToArray();
    }

    private void AddProfitLossSection(Document doc, ProfitLossReport pl)
    {
        doc.Add(SectionTitle("P&L — Прибыли и убытки"));
        doc.Add(Line($"Доходы: {pl.TotalIncome:N0}{BynCurrency.Suffix}"));
        doc.Add(Line($"Расходы: {Math.Abs(pl.TotalExpense):N0}{BynCurrency.Suffix}"));
        doc.Add(Line($"Чистая прибыль: {pl.NetProfit:N0}{BynCurrency.Suffix} ({pl.ProfitMargin:F1}%)", bold: true));
        if (pl.ExpenseByCategory.Any())
        {
            doc.Add(SectionTitle("Структура расходов").SetFontSize(12).SetMarginTop(12));
            doc.Add(BuildCategoryTable(pl.ExpenseByCategory.Select(c => (c.Category, c.Amount, c.Percentage))));
        }
        if (pl.IncomeByCategory.Any())
        {
            doc.Add(SectionTitle("Доходы по категориям").SetFontSize(12).SetMarginTop(12));
            doc.Add(BuildCategoryTable(pl.IncomeByCategory.Select(c => (c.Category, c.Amount, c.Percentage))));
        }
    }

    private void AddCashFlowSection(Document doc, CashFlowStatementReport cf)
    {
        doc.Add(SectionTitle("Движение денежных средств"));
        doc.Add(Line($"Операционная: {cf.OperatingCashFlow:N0}{BynCurrency.Suffix}"));
        doc.Add(Line($"Инвестиционная: {cf.InvestmentCashFlow:N0}{BynCurrency.Suffix}"));
        doc.Add(Line($"Финансовая: {cf.FinancingCashFlow:N0}{BynCurrency.Suffix}"));
        doc.Add(Line($"Чистый поток: {cf.NetCashFlow:N0}{BynCurrency.Suffix}", bold: true));
        if (cf.CategoryDetails.Any())
        {
            doc.Add(Spacer());
            var t = new Table(UnitValue.CreatePercentArray(new float[] { 40, 20, 20, 20 })).UseAllAvailableWidth();
            t.AddHeaderCell(H("Категория"));
            t.AddHeaderCell(H("Приход"));
            t.AddHeaderCell(H("Расход"));
            t.AddHeaderCell(H("Нетто"));
            foreach (var c in cf.CategoryDetails)
            {
                t.AddCell(D(c.Category));
                t.AddCell(N(c.Income));
                t.AddCell(N(c.Expense));
                t.AddCell(N(c.NetFlow));
            }
            doc.Add(t);
        }
    }

    private Table BuildKpiTable(IReadOnlyList<KpiMetricDto> kpi, string compareMode)
    {
        var kpiTable = new Table(UnitValue.CreatePercentArray(new float[] { 38, 32, 30 })).UseAllAvailableWidth();
        kpiTable.AddHeaderCell(H("Показатель"));
        kpiTable.AddHeaderCell(H("Значение"));
        kpiTable.AddHeaderCell(H("Δ %"));
        foreach (var k in kpi)
        {
            kpiTable.AddCell(D(k.Label));
            kpiTable.AddCell(D(k.Format == "percent" ? $"{k.Value:F1}%" : $"{Fmt(k.Value)}{BynCurrency.Suffix}", TextAlignment.RIGHT));
            var sign = k.ChangePercent >= 0 ? "+" : "";
            kpiTable.AddCell(D(compareMode == "none" ? "-" : $"{sign}{k.ChangePercent:F1}%", TextAlignment.RIGHT));
        }
        return kpiTable;
    }

    private Table BuildTrialBalanceTable(TrialBalanceReport report)
    {
        var t = new Table(UnitValue.CreatePercentArray(new float[] { 32, 17, 17, 17, 17 })).UseAllAvailableWidth();
        t.AddHeaderCell(H("Категория"));
        t.AddHeaderCell(H("Сальдо нач."));
        t.AddHeaderCell(H("Дебет"));
        t.AddHeaderCell(H("Кредит"));
        t.AddHeaderCell(H("Сальдо кон."));
        foreach (var e in report.Entries)
        {
            t.AddCell(D(e.Category));
            t.AddCell(N(e.OpeningBalance));
            t.AddCell(N(e.Debit));
            t.AddCell(N(e.Credit));
            t.AddCell(N(e.ClosingBalance));
        }
        return t;
    }

    private Table BuildIncomeBookTable(IReadOnlyList<IncomeExpenseBookEntry> entries)
    {
        var t = new Table(UnitValue.CreatePercentArray(new float[] { 7, 13, 12, 20, 28, 10, 10 })).UseAllAvailableWidth();
        t.AddHeaderCell(H("№"));
        t.AddHeaderCell(H("Дата"));
        t.AddHeaderCell(H("Док."));
        t.AddHeaderCell(H("Контрагент"));
        t.AddHeaderCell(H("Содержание"));
        t.AddHeaderCell(H("Доход"));
        t.AddHeaderCell(H("Расход"));
        foreach (var e in entries)
        {
            t.AddCell(D(e.EntryNumber.ToString(), TextAlignment.CENTER));
            t.AddCell(D(e.Date.ToString("dd.MM.yyyy")));
            t.AddCell(D(e.DocumentNumber));
            t.AddCell(D(e.Counterparty));
            t.AddCell(D(e.Description));
            t.AddCell(N(e.Income));
            t.AddCell(N(e.Expense));
        }
        return t;
    }

    private Table BuildProfitabilityTable(IReadOnlyList<ProfitabilityMatrixRowDto> rows)
    {
        var t = new Table(UnitValue.CreatePercentArray(new float[] { 28, 14, 12, 14, 12, 12, 14 })).UseAllAvailableWidth();
        t.AddHeaderCell(H("Проект"));
        t.AddHeaderCell(H("Выручка"));
        t.AddHeaderCell(H("Маржа%"));
        t.AddHeaderCell(H("Зарплаты"));
        t.AddHeaderCell(H("Аренда"));
        t.AddHeaderCell(H("Налоги"));
        t.AddHeaderCell(H("Чистый доход"));
        foreach (var row in rows)
        {
            t.AddCell(D(row.Name));
            t.AddCell(N(row.Revenue));
            t.AddCell(D($"{row.MarginPercent:F1}%", TextAlignment.RIGHT));
            t.AddCell(N(row.Payroll));
            t.AddCell(N(row.Rent));
            t.AddCell(N(row.Taxes));
            t.AddCell(N(row.NetIncome));
        }
        return t;
    }

    private Table BuildForecastTable(CashForecastChartDto forecast)
    {
        var ft = new Table(UnitValue.CreatePercentArray(new float[] { 30, 35, 35 })).UseAllAvailableWidth();
        ft.AddHeaderCell(H("Дата"));
        ft.AddHeaderCell(H("Баланс"));
        ft.AddHeaderCell(H("Риск"));
        var points = forecast.Points;
        for (var i = 0; i < points.Count; i++)
        {
            if (i % 7 != 0 && i != points.Count - 1) continue;
            var p = points[i];
            ft.AddCell(D(p.Date.ToString("dd.MM.yyyy")));
            ft.AddCell(D($"{Fmt(p.Balance)}{BynCurrency.Suffix}", TextAlignment.RIGHT));
            ft.AddCell(D(p.IsGap ? "!" : "-", TextAlignment.CENTER));
        }
        return ft;
    }

    private Table BuildTransactionsTable(IReadOnlyList<TransactionPdfRow> rows, bool includeTaxColumns = false)
    {
        includeTaxColumns = includeTaxColumns || rows.Any(r => r.AccruedTax is > 0 || !string.IsNullOrEmpty(r.TaxNote));

        if (includeTaxColumns)
        {
            var t = new Table(UnitValue.CreatePercentArray(new float[] { 9, 22, 14, 11, 11, 12, 21 })).UseAllAvailableWidth();
            t.AddHeaderCell(H("Дата"));
            t.AddHeaderCell(H("Описание"));
            t.AddHeaderCell(H("Категория"));
            t.AddHeaderCell(H("Сумма"));
            t.AddHeaderCell(H("Налог"));
            t.AddHeaderCell(H("Ставка"));
            t.AddHeaderCell(H("Примечание"));
            foreach (var row in rows)
            {
                t.AddCell(D(row.Date.ToString("dd.MM.yyyy")));
                t.AddCell(D(row.Description));
                t.AddCell(D(row.Category));
                t.AddCell(N(row.Amount, decimals: true));
                t.AddCell(row.AccruedTax is > 0 ? N(row.AccruedTax.Value, decimals: true) : D("—", TextAlignment.CENTER));
                t.AddCell(D(row.TaxNote?.Contains('%') == true ? ExtractRate(row.TaxNote) : "—", TextAlignment.CENTER));
                t.AddCell(D(row.TaxNote, size: 8));
            }
            return t;
        }

        var std = new Table(UnitValue.CreatePercentArray(new float[] { 11, 30, 18, 13, 14, 14 })).UseAllAvailableWidth();
        std.AddHeaderCell(H("Дата"));
        std.AddHeaderCell(H("Описание"));
        std.AddHeaderCell(H("Категория"));
        std.AddHeaderCell(H("Сумма"));
        std.AddHeaderCell(H("Проект"));
        std.AddHeaderCell(H("Контрагент"));
        foreach (var row in rows)
        {
            std.AddCell(D(row.Date.ToString("dd.MM.yyyy")));
            std.AddCell(D(row.Description));
            std.AddCell(D(row.Category));
            std.AddCell(N(row.Amount, decimals: true));
            std.AddCell(D(row.Project));
            std.AddCell(D(row.Counterparty));
        }
        return std;
    }

    private static string ExtractRate(string? note)
    {
        if (string.IsNullOrEmpty(note)) return "—";
        if (note.Contains("6%", StringComparison.Ordinal)) return "6%";
        if (note.Contains("4%", StringComparison.Ordinal)) return "4%";
        if (note.Contains("8%", StringComparison.Ordinal)) return "8%";
        if (note.Contains("20/120", StringComparison.Ordinal)) return "20/120";
        if (note.Contains("16%", StringComparison.Ordinal)) return "16%";
        return "—";
    }

    private static string Fmt(decimal value, bool decimals = false) =>
        value.ToString(decimals ? "#,##0.00" : "#,##0", CultureInfo.InvariantCulture);

    private Cell D(string? text, TextAlignment align = TextAlignment.LEFT, float size = 9, bool bold = false)
    {
        var display = string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();
        return new Cell()
            .Add(BuildTextParagraph(display, bold, size))
            .SetTextAlignment(align)
            .SetVerticalAlignment(VerticalAlignment.MIDDLE)
            .SetPaddingTop(4)
            .SetPaddingBottom(4)
            .SetPaddingLeft(5)
            .SetPaddingRight(5)
            .SetBorder(Border.NO_BORDER);
    }

    private Cell N(decimal value, bool decimals = false) =>
        D(Fmt(value, decimals), TextAlignment.RIGHT);

    private Paragraph FooterParagraph() =>
        new Paragraph($"— MiniFinance · {DateTime.Now:dd.MM.yyyy HH:mm} —")
            .SetFont(_fontRegular).SetFontSize(8).SetFontColor(ColorConstants.GRAY)
            .SetTextAlignment(TextAlignment.CENTER);

    private Paragraph SectionTitle(string text) =>
        new Paragraph(text).SetFont(_fontBold).SetFontSize(14).SetMarginBottom(6);

    private Paragraph Line(string text, bool bold = false, float size = 11, Color? color = null)
    {
        var p = BuildTextParagraph(text, bold, size);
        if (color != null) p.SetFontColor(color);
        return p;
    }

    private Paragraph BuildTextParagraph(string text, bool bold, float size)
    {
        if (TrySplitBynSuffix(text, out var main))
        {
            var p = new Paragraph().SetFontSize(size);
            p.Add(new Text(main).SetFont(bold ? _fontBold : _fontRegular));
            p.Add(new Text(BynCurrency.SymbolString).SetFont(_fontByn));
            return p;
        }

        return new Paragraph(text).SetFont(bold ? _fontBold : _fontRegular).SetFontSize(size);
    }

    private static bool TrySplitBynSuffix(string text, out string main)
    {
        if (text.EndsWith(BynCurrency.Suffix, StringComparison.Ordinal))
        {
            main = text[..^BynCurrency.Suffix.Length];
            return true;
        }

        main = text;
        return false;
    }

    private static PdfFont LoadBynFont()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts", "nbrb", "nbrb.ttf");
        if (File.Exists(path))
        {
            try
            {
                return PdfFontFactory.CreateFont(path, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
            }
            catch { /* fallback */ }
        }

        return PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
    }

    private static Paragraph Spacer(float size = 8) => new Paragraph(" ").SetFontSize(size);

    private Cell H(string text) =>
        D(text, bold: true, size: 10).SetBackgroundColor(HeaderBg);

    private Table BuildCategoryTable(IEnumerable<(string Category, decimal Amount, decimal Pct)> rows)
    {
        var table = new Table(UnitValue.CreatePercentArray(new float[] { 50, 30, 20 })).UseAllAvailableWidth();
        table.AddHeaderCell(H("Категория"));
        table.AddHeaderCell(H("Сумма"));
        table.AddHeaderCell(H("%"));
        foreach (var (cat, amt, pct) in rows)
        {
            table.AddCell(D(cat));
            table.AddCell(D($"{Fmt(amt)}{BynCurrency.Suffix}", TextAlignment.RIGHT));
            table.AddCell(D($"{pct:F0}%", TextAlignment.RIGHT));
        }
        return table;
    }

    private byte[] BuildPdf(string title, DateTime start, DateTime end, Action<Document> buildBody)
    {
        using var ms = new MemoryStream();
        using var writer = CreateWriter(ms);
        using var pdf = new PdfDocument(writer);
        using var doc = new Document(pdf);
        doc.SetMargins(48, 48, 72, 48);

        doc.Add(new Paragraph("MiniFinance").SetFont(_fontRegular).SetFontSize(10).SetFontColor(ColorConstants.GRAY));
        doc.Add(SectionTitle(title).SetFontSize(16));
        doc.Add(Line($"Период: {start:dd.MM.yyyy} — {end:dd.MM.yyyy}", size: 10, color: ColorConstants.GRAY));
        doc.Add(Line($"Сформирован: {DateTime.Now:dd.MM.yyyy HH:mm}", size: 9, color: ColorConstants.GRAY));
        doc.Add(Spacer(6));
        buildBody(doc);
        doc.Close();
        return ms.ToArray();
    }

    private static PdfWriter CreateWriter(Stream ms) => new(ms);

    private static PdfFont LoadFont(bool bold)
    {
        var path = ResolveFontPath(bold);
        if (path != null)
        {
            try
            {
                return PdfFontFactory.CreateFont(path, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
            }
            catch { /* fallback */ }
        }
        return PdfFontFactory.CreateFont(bold ? StandardFonts.HELVETICA_BOLD : StandardFonts.HELVETICA);
    }

    private static string? ResolveFontPath(bool bold)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts",
            bold ? "DejaVuSans-Bold.ttf" : "DejaVuSans.ttf");
        if (File.Exists(bundled)) return bundled;

        var winFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var candidates = bold
            ? new[] { "arialbd.ttf", "Arial Bold.ttf", "segoeuib.ttf" }
            : new[] { "arial.ttf", "Arial.ttf", "segoeui.ttf", "ARIALUNI.TTF" };
        foreach (var c in candidates)
        {
            var p = Path.Combine(winFonts, c);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public byte[] GenerateTaxPackagePdf(TaxExportDocument doc) =>
        BuildPdf("Налоговый пакет для специалиста (РБ)", doc.PeriodStart, doc.PeriodEnd, body =>
        {
            if (!string.IsNullOrWhiteSpace(doc.CompanyName))
                body.Add(Line($"Организация: {doc.CompanyName}", bold: true));
            if (!string.IsNullOrWhiteSpace(doc.Unp))
                body.Add(Line($"УНП: {doc.Unp}"));
            body.Add(Line($"Режим: {TaxSystemInfo.GetLabel(doc.TaxSystem)}"));
            if (doc.TaxSystem == TaxSystem.OSN)
            {
                var kind = doc.TaxpayerKind == TaxpayerKind.IndividualEntrepreneur ? "ИП (подоходный)" : "Юридическое лицо (налог на прибыль)";
                body.Add(Line($"Налогоплательщик: {kind}", size: 10));
            }
            body.Add(Spacer(8));

            body.Add(SectionTitle("Сводка по учёту за период"));
            body.Add(Line($"Доход (налоговая база): {doc.Totals.Income:N2}{BynCurrency.Suffix}"));
            body.Add(Line($"Расходы (вычитаемые): {doc.Totals.Expenses:N2}{BynCurrency.Suffix}"));
            body.Add(Line($"Финансовый результат: {doc.Totals.Profit:N2}{BynCurrency.Suffix}"));
            body.Add(Line($"Операций: {doc.Totals.OperationCount} (исключено из базы: {doc.Totals.ExcludedCount})"));
            if (doc.Totals.AccruedTax > 0)
                body.Add(Line($"Начислено по операциям: {doc.Totals.AccruedTax:N2}{BynCurrency.Suffix}", bold: true));
            body.Add(Spacer(8));

            if (doc.Analysis != null && doc.TaxSystem == TaxSystem.NPD)
            {
                body.Add(Line(
                    $"НПД: физлица {doc.Analysis.IncomeFromIndividuals:N2}{BynCurrency.Suffix}, " +
                    $"юрлица/ИП {doc.Analysis.IncomeFromLegalEntities:N2}{BynCurrency.Suffix}", size: 10));
                body.Add(Spacer(6));
            }

            if (doc.Estimate != null && doc.Estimate.TaxAmount > 0)
            {
                body.Add(SectionTitle("Итоговый расчёт налога"));
                body.Add(Line(doc.Estimate.Description, size: 10));
                foreach (var line in doc.Estimate.Lines)
                    body.Add(Line($"  · {line.Name}: {line.Amount:N2}{BynCurrency.Suffix}", size: 10));
                body.Add(Line($"Итого к уплате (ориентир): {doc.Estimate.TaxAmount:N2}{BynCurrency.Suffix}", bold: true));
                body.Add(Spacer(8));
            }

            body.Add(SectionTitle("Плановые налоговые платежи"));
            if (doc.Payments.Count == 0)
                body.Add(Line("Нет записей за период", size: 10));
            else
            {
                var pt = new Table(UnitValue.CreatePercentArray(new float[] { 28, 22, 18, 32 })).UseAllAvailableWidth();
                pt.AddHeaderCell(H("Название"));
                pt.AddHeaderCell(H("Срок"));
                pt.AddHeaderCell(H("Сумма"));
                pt.AddHeaderCell(H("Статус"));
                foreach (var p in doc.Payments)
                {
                    pt.AddCell(D(p.Name));
                    pt.AddCell(D(p.DueDate.ToString("dd.MM.yyyy")));
                    pt.AddCell(N(p.Amount, decimals: true));
                    pt.AddCell(D(TaxCalculatorHelper.GetPaymentStatusLabel(p)));
                }
                body.Add(pt);
            }
            body.Add(Spacer(10));

            body.Add(SectionTitle("Расчёт по каждой операции"));
            body.Add(Line("Каждая подтверждённая операция с указанием налоговой базы и начисленного налога.", size: 9, color: ColorConstants.GRAY));
            if (doc.Transactions.Count == 0)
                body.Add(Line("Нет операций", size: 10));
            else
                body.Add(BuildTransactionsTable(doc.Transactions, includeTaxColumns: true));

            body.Add(Spacer(12));
            body.Add(Line(TaxSystemInfo.GetMnsHint(), size: 8, color: ColorConstants.GRAY));
        });

}

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

    public ReportPdfService()
    {
        _fontRegular = LoadFont(bold: false);
        _fontBold = LoadFont(bold: true);
    }

    public byte[] GenerateProfitLossPdf(ProfitLossReport report, DateTime start, DateTime end) =>
        BuildPdf("Отчёт о прибылях и убытках (P&L)", start, end, body =>
        {
            body.Add(Line($"Доходы: {report.TotalIncome:N0} Br"));
            body.Add(Line($"Расходы: {Math.Abs(report.TotalExpense):N0} Br"));
            body.Add(Line($"Чистая прибыль: {report.NetProfit:N0} Br ({report.ProfitMargin:F1}%)", bold: true));
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
            body.Add(Line($"Операционная: {report.OperatingCashFlow:N0} Br"));
            body.Add(Line($"Инвестиционная: {report.InvestmentCashFlow:N0} Br"));
            body.Add(Line($"Финансовая: {report.FinancingCashFlow:N0} Br"));
            body.Add(Line($"Чистый поток: {report.NetCashFlow:N0} Br", bold: true));
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
                    table.AddCell(c.Category);
                    table.AddCell($"{c.Income:N0}");
                    table.AddCell($"{c.Expense:N0}");
                    table.AddCell($"{c.NetFlow:N0}");
                }
                body.Add(table);
            }
        });

    public byte[] GenerateFullReportPdf(ReportAnalyticsSnapshot snapshot)
    {
        var f = snapshot.Filters;
        using var ms = new MemoryStream();
        using var writer = CreateWriter(ms);
        using var pdf = new PdfDocument(writer);
        using var doc = new Document(pdf);
        doc.SetMargins(48, 48, 72, 48);

        doc.Add(new Paragraph("MiniFinance").SetFont(_fontBold).SetFontSize(22).SetFontColor(Accent));
        doc.Add(SectionTitle("Финансовый отчёт").SetFontSize(18));
        doc.Add(Line($"Период: {f.Start:dd.MM.yyyy} — {f.End:dd.MM.yyyy}", size: 10, color: ColorConstants.GRAY));
        doc.Add(Line($"Сформировано: {DateTime.Now:dd.MM.yyyy HH:mm}", size: 9, color: ColorConstants.GRAY));
        if (f.ProjectId.HasValue)
            doc.Add(Line($"Проект ID: {f.ProjectId}", size: 9, color: ColorConstants.DARK_GRAY));
        if (!string.IsNullOrEmpty(snapshot.SelectedCategory))
            doc.Add(Line($"Категория: {snapshot.SelectedCategory}", size: 9, color: ColorConstants.DARK_GRAY));
        doc.Add(Spacer(12));

        doc.Add(SectionTitle("Ключевые показатели"));
        var kpiTable = new Table(UnitValue.CreatePercentArray(new float[] { 38, 32, 30 })).UseAllAvailableWidth();
        kpiTable.AddHeaderCell(H("Показатель"));
        kpiTable.AddHeaderCell(H("Значение"));
        kpiTable.AddHeaderCell(H("Δ %"));
        foreach (var k in snapshot.Kpi)
        {
            kpiTable.AddCell(k.Label);
            kpiTable.AddCell(k.Format == "percent" ? $"{k.Value:F1}%" : $"{k.Value:N0} Br");
            var sign = k.ChangePercent >= 0 ? "+" : "";
            kpiTable.AddCell(f.CompareMode == "none" ? "—" : $"{sign}{k.ChangePercent:F1}%");
        }
        doc.Add(kpiTable);
        doc.Add(Line($"Налоговый резерв: {snapshot.TaxReserve:N0} Br  |  Свободные деньги: {snapshot.FreeCash:N0} Br")
            .SetMarginTop(10));

        if (snapshot.ProfitLoss != null)
        {
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            doc.Add(SectionTitle("P&L — Прибыли и убытки"));
            var pl = snapshot.ProfitLoss;
            doc.Add(Line($"Доходы: {pl.TotalIncome:N0} Br"));
            doc.Add(Line($"Расходы: {Math.Abs(pl.TotalExpense):N0} Br"));
            doc.Add(Line($"Чистая прибыль: {pl.NetProfit:N0} Br ({pl.ProfitMargin:F1}%)", bold: true));
            if (pl.ExpenseByCategory.Any())
            {
                doc.Add(SectionTitle("Структура расходов").SetFontSize(12).SetMarginTop(12));
                doc.Add(BuildCategoryTable(pl.ExpenseByCategory.Select(c => (c.Category, c.Amount, c.Percentage))));
            }
        }

        if (snapshot.CashFlow != null)
        {
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            doc.Add(SectionTitle("Cash Flow"));
            var cf = snapshot.CashFlow;
            doc.Add(Line($"Операционная: {cf.OperatingCashFlow:N0} Br"));
            doc.Add(Line($"Инвестиционная: {cf.InvestmentCashFlow:N0} Br"));
            doc.Add(Line($"Финансовая: {cf.FinancingCashFlow:N0} Br"));
            doc.Add(Line($"Чистый поток: {cf.NetCashFlow:N0} Br", bold: true));
            if (cf.CategoryDetails.Any())
            {
                doc.Add(Spacer());
                var t = new Table(UnitValue.CreatePercentArray(new float[] { 40, 20, 20, 20 })).UseAllAvailableWidth();
                t.AddHeaderCell(H("Категория"));
                t.AddHeaderCell(H("Приход"));
                t.AddHeaderCell(H("Расход"));
                t.AddHeaderCell(H("Нетто"));
                foreach (var c in cf.CategoryDetails.Take(20))
                {
                    t.AddCell(c.Category);
                    t.AddCell($"{c.Income:N0}");
                    t.AddCell($"{c.Expense:N0}");
                    t.AddCell($"{c.NetFlow:N0}");
                }
                doc.Add(t);
            }
        }

        if (snapshot.ProfitabilityMatrix.Any())
        {
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            doc.Add(SectionTitle("Рентабельность проектов"));
            var t = new Table(UnitValue.CreatePercentArray(new float[] { 28, 14, 12, 14, 12, 12, 14 })).UseAllAvailableWidth();
            t.AddHeaderCell(H("Проект"));
            t.AddHeaderCell(H("Выручка"));
            t.AddHeaderCell(H("Маржа%"));
            t.AddHeaderCell(H("Зарплаты"));
            t.AddHeaderCell(H("Аренда"));
            t.AddHeaderCell(H("Налоги"));
            t.AddHeaderCell(H("Чистый доход"));
            foreach (var row in snapshot.ProfitabilityMatrix.Take(15))
            {
                t.AddCell(row.Name);
                t.AddCell($"{row.Revenue:N0}");
                t.AddCell($"{row.MarginPercent:F1}");
                t.AddCell($"{row.Payroll:N0}");
                t.AddCell($"{row.Rent:N0}");
                t.AddCell($"{row.Taxes:N0}");
                t.AddCell($"{row.NetIncome:N0}");
            }
            doc.Add(t);
        }

        if (snapshot.Forecast.Points.Any())
        {
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            doc.Add(SectionTitle($"Прогноз ликвидности ({snapshot.Forecast.HorizonDays} дн.)"));
            doc.Add(Line($"Текущий остаток: {snapshot.Forecast.CurrentBalance:N0} Br"));
            if (snapshot.Forecast.HasRisk)
                doc.Add(Line($"⚠ Риск кассового разрыва — мин. остаток: {snapshot.Forecast.MinBalance:N0} Br", color: Danger));
            else
                doc.Add(Line("Критических зон не выявлено"));

            var ft = new Table(UnitValue.CreatePercentArray(new float[] { 30, 35, 35 })).UseAllAvailableWidth();
            ft.AddHeaderCell(H("Дата"));
            ft.AddHeaderCell(H("Баланс"));
            ft.AddHeaderCell(H("Риск"));
            foreach (var p in snapshot.Forecast.Points.Where((_, i) => i % 7 == 0 || i == snapshot.Forecast.Points.Count - 1).Take(20))
            {
                ft.AddCell(p.Date.ToString("dd.MM.yyyy"));
                ft.AddCell($"{p.Balance:N0} Br");
                ft.AddCell(p.IsGap ? "⚠" : "");
            }
            doc.Add(ft.SetMarginTop(12));
        }

        if (snapshot.CriticalReminders.Any())
        {
            doc.Add(Spacer());
            doc.Add(SectionTitle("Налоговый календарь").SetFontSize(12));
            foreach (var a in snapshot.CriticalReminders.Take(6))
                doc.Add(Line($"• {a.Title} — {a.DueDate:dd.MM.yyyy}: {a.Amount:N0} Br ({a.Severity})"));
        }

        doc.Add(Spacer(16));
        doc.Add(new Paragraph($"— MiniFinance · {DateTime.Now:dd.MM.yyyy HH:mm} —")
            .SetFont(_fontRegular).SetFontSize(8).SetFontColor(ColorConstants.GRAY)
            .SetTextAlignment(TextAlignment.CENTER));
        doc.Close();
        return ms.ToArray();
    }

    private Paragraph SectionTitle(string text) =>
        new Paragraph(text).SetFont(_fontBold).SetFontSize(14).SetMarginBottom(6);

    private Paragraph Line(string text, bool bold = false, float size = 11, Color? color = null)
    {
        var p = new Paragraph(text).SetFont(bold ? _fontBold : _fontRegular).SetFontSize(size);
        if (color != null) p.SetFontColor(color);
        return p;
    }

    private static Paragraph Spacer(float size = 8) => new Paragraph(" ").SetFontSize(size);

    private Cell H(string text) =>
        new Cell().Add(new Paragraph(text).SetFont(_fontBold).SetFontSize(10))
            .SetBackgroundColor(HeaderBg).SetBorder(Border.NO_BORDER);

    private Table BuildCategoryTable(IEnumerable<(string Category, decimal Amount, decimal Pct)> rows)
    {
        var table = new Table(UnitValue.CreatePercentArray(new float[] { 50, 30, 20 })).UseAllAvailableWidth();
        table.AddHeaderCell(H("Категория"));
        table.AddHeaderCell(H("Сумма"));
        table.AddHeaderCell(H("%"));
        foreach (var (cat, amt, pct) in rows)
        {
            table.AddCell(cat);
            table.AddCell($"{amt:N0} Br");
            table.AddCell($"{pct:F0}%");
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

}

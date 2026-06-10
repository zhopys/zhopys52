using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public sealed class TaxExportService : ITaxExportService
{
    private readonly ApplicationDbContext _db;
    private readonly ITaxFinanceSummaryService _summary;
    private readonly IReportPdfService _pdf;
    private readonly IDataScopeService _dataScope;

    public TaxExportService(
        ApplicationDbContext db,
        ITaxFinanceSummaryService summary,
        IReportPdfService pdf,
        IDataScopeService dataScope)
    {
        _db = db;
        _summary = summary;
        _pdf = pdf;
        _dataScope = dataScope;
    }

    public async Task<(byte[] Data, string FileName)> BuildTaxPackagePdfAsync(string ownerUserId, DateTime start, DateTime end)
    {
        ownerUserId = await ServiceDataScope.ResolveAsync(_dataScope, ownerUserId);
        if (end < start)
            (start, end) = (end, start);

        var settings = await _db.OrganizationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == ownerUserId);

        var taxSystem = settings?.TaxSystem ?? TaxSystem.USN;
        var taxpayerKind = settings?.TaxpayerKind ?? TaxpayerKind.LegalEntity;

        var analysis = await _summary.GetPeriodAnalysisAsync(
            ownerUserId, start, end, taxSystem, taxpayerKind);

        var totals = new TaxPeriodTotalsDto
        {
            PeriodStart = start,
            PeriodEnd = end,
            Income = analysis.Income,
            Expenses = analysis.Expenses,
            OperationCount = analysis.OperationCount,
            ExcludedCount = analysis.ExcludedCount,
            AccruedTax = analysis.AccruedTaxTotal
        };

        var payments = await _db.TaxPayments.AsNoTracking()
            .Where(t => t.UserId == ownerUserId && t.DueDate >= start && t.DueDate <= end)
            .OrderBy(t => t.DueDate)
            .ToListAsync();

        var txs = analysis.Lines.Select(l =>
        {
            var note = string.IsNullOrEmpty(l.RateLabel) ? l.Note : $"{l.RateLabel} · {l.Note}";
            return new TransactionPdfRow(
                l.Date,
                l.Description,
                l.Category,
                l.Amount,
                null,
                l.Counterparty,
                l.AccruedTax > 0 ? l.AccruedTax : null,
                string.IsNullOrWhiteSpace(note) ? null : note);
        }).ToList();

        var doc = new TaxExportDocument
        {
            CompanyName = settings?.CompanyName ?? "",
            Unp = settings?.UNP ?? "",
            TaxSystem = taxSystem,
            TaxpayerKind = taxpayerKind,
            PeriodStart = start,
            PeriodEnd = end,
            Totals = totals,
            Analysis = analysis,
            Estimate = analysis.Calculation.TaxAmount > 0 || !string.IsNullOrEmpty(analysis.Calculation.Description)
                ? analysis.Calculation
                : null,
            Payments = payments,
            Transactions = txs
        };

        var bytes = _pdf.GenerateTaxPackagePdf(doc);
        var fileName = $"nalogi-rb-{start:yyyyMMdd}-{end:yyyyMMdd}.pdf";
        return (bytes, fileName);
    }
}

public sealed class TaxExportDocument
{
    public string CompanyName { get; init; } = "";
    public string Unp { get; init; } = "";
    public TaxSystem TaxSystem { get; init; }
    public TaxpayerKind TaxpayerKind { get; init; }
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public TaxPeriodTotalsDto Totals { get; init; } = new();
    public TaxPeriodAnalysisDto? Analysis { get; init; }
    public TaxCalculationResult? Estimate { get; init; }
    public IReadOnlyList<TaxPayment> Payments { get; init; } = Array.Empty<TaxPayment>();
    public IReadOnlyList<TransactionPdfRow> Transactions { get; init; } = Array.Empty<TransactionPdfRow>();
}

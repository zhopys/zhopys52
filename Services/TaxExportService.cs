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

        var totals = await _summary.GetPeriodTotalsAsync(ownerUserId, start, end);

        var calc = TaxCalculatorHelper.Calculate(new TaxCalculatorInput
        {
            System = settings?.TaxSystem ?? TaxSystem.USN,
            TaxpayerKind = settings?.TaxpayerKind ?? TaxpayerKind.LegalEntity,
            Income = totals.Income,
            Expenses = totals.Expenses
        });

        var payments = await _db.TaxPayments.AsNoTracking()
            .Where(t => t.UserId == ownerUserId && t.DueDate >= start && t.DueDate <= end)
            .OrderBy(t => t.DueDate)
            .ToListAsync();

        var txs = await _db.Transactions.AsNoTracking()
            .Where(t => t.UserId == ownerUserId && t.IsConfirmed && t.Date >= start && t.Date <= end)
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .Select(t => new TransactionPdfRow(
                t.Date,
                t.Description,
                t.Category,
                t.Amount,
                null,
                t.Counterparty))
            .ToListAsync();

        var doc = new TaxExportDocument
        {
            CompanyName = settings?.CompanyName ?? "",
            Unp = settings?.UNP ?? "",
            TaxSystem = settings?.TaxSystem ?? TaxSystem.USN,
            TaxpayerKind = settings?.TaxpayerKind ?? TaxpayerKind.LegalEntity,
            PeriodStart = start,
            PeriodEnd = end,
            Totals = totals,
            Estimate = calc.TaxAmount > 0 || !string.IsNullOrEmpty(calc.Description) ? calc : null,
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
    public TaxCalculationResult? Estimate { get; init; }
    public IReadOnlyList<TaxPayment> Payments { get; init; } = Array.Empty<TaxPayment>();
    public IReadOnlyList<TransactionPdfRow> Transactions { get; init; } = Array.Empty<TransactionPdfRow>();
}

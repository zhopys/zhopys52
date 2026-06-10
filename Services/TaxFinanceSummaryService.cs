using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public sealed class TaxFinanceSummaryService : ITaxFinanceSummaryService
{
    private readonly ApplicationDbContext _db;
    private readonly IDataScopeService _dataScope;
    private readonly ITransactionTaxService _transactionTax;

    public TaxFinanceSummaryService(
        ApplicationDbContext db,
        IDataScopeService dataScope,
        ITransactionTaxService transactionTax)
    {
        _db = db;
        _dataScope = dataScope;
        _transactionTax = transactionTax;
    }

    public async Task<TaxPeriodTotalsDto> GetPeriodTotalsAsync(string ownerUserId, DateTime start, DateTime end)
    {
        var settings = await GetOrgSettingsAsync(ownerUserId);
        var analysis = await _transactionTax.AnalyzePeriodAsync(
            ownerUserId, start, end,
            settings?.TaxSystem ?? TaxSystem.USN,
            settings?.TaxpayerKind ?? TaxpayerKind.LegalEntity);

        return new TaxPeriodTotalsDto
        {
            PeriodStart = start,
            PeriodEnd = end,
            Income = analysis.Income,
            Expenses = analysis.Expenses,
            OperationCount = analysis.OperationCount,
            ExcludedCount = analysis.ExcludedCount,
            AccruedTax = analysis.AccruedTaxTotal
        };
    }

    public Task<TaxPeriodAnalysisDto> GetPeriodAnalysisAsync(
        string ownerUserId,
        DateTime start,
        DateTime end,
        TaxSystem taxSystem,
        TaxpayerKind taxpayerKind,
        bool includeFsznEstimate = false) =>
        _transactionTax.AnalyzePeriodAsync(ownerUserId, start, end, taxSystem, taxpayerKind, includeFsznEstimate);

    public async Task<decimal> GetTaxCategoryPaidYearToDateAsync(string ownerUserId, DateTime yearStart)
    {
        ownerUserId = await ServiceDataScope.ResolveAsync(_dataScope, ownerUserId);
        return await _db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == ownerUserId
                        && t.IsConfirmed
                        && t.Date >= yearStart
                        && t.Amount < 0
                        && (t.Category == "Налоги" || EF.Functions.Like(t.Category, "%налог%")))
            .SumAsync(t => -t.Amount);
    }

    public async Task<IReadOnlyList<DateTime>> GetAvailablePeriodAnchorsAsync(string ownerUserId, string periodType)
    {
        ownerUserId = await ServiceDataScope.ResolveAsync(_dataScope, ownerUserId);
        var dates = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == ownerUserId && t.IsConfirmed)
            .Select(t => t.Date)
            .ToListAsync();

        if (dates.Count == 0)
            return new[] { new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1) };

        return periodType switch
        {
            "Year" => dates
                .GroupBy(d => d.Year)
                .Select(g => new DateTime(g.Key, 1, 1))
                .OrderByDescending(d => d)
                .ToList(),
            "Quarter" => dates
                .GroupBy(d => new { d.Year, Q = (d.Month - 1) / 3 })
                .Select(g => new DateTime(g.Key.Year, g.Key.Q * 3 + 1, 1))
                .OrderByDescending(d => d)
                .ToList(),
            _ => dates
                .GroupBy(d => new { d.Year, d.Month })
                .Select(g => new DateTime(g.Key.Year, g.Key.Month, 1))
                .OrderByDescending(d => d)
                .ToList()
        };
    }

    private async Task<OrganizationSettings?> GetOrgSettingsAsync(string ownerUserId)
    {
        ownerUserId = await ServiceDataScope.ResolveAsync(_dataScope, ownerUserId);
        return await _db.OrganizationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == ownerUserId);
    }
}

using MiniFinance.Data.Models;

namespace MiniFinance.Services;

/// <summary>Сводные финансовые показатели для налоговых расчётов.</summary>
public interface ITaxFinanceSummaryService
{
    Task<TaxPeriodTotalsDto> GetPeriodTotalsAsync(string ownerUserId, DateTime start, DateTime end);
    Task<TaxPeriodAnalysisDto> GetPeriodAnalysisAsync(
        string ownerUserId,
        DateTime start,
        DateTime end,
        TaxSystem taxSystem,
        TaxpayerKind taxpayerKind,
        bool includeFsznEstimate = false);
    Task<decimal> GetTaxCategoryPaidYearToDateAsync(string ownerUserId, DateTime yearStart);
    /// <summary>Якоря периодов для фильтров отчётов (без списка операций).</summary>
    Task<IReadOnlyList<DateTime>> GetAvailablePeriodAnchorsAsync(string ownerUserId, string periodType);
}

public sealed class TaxPeriodTotalsDto
{
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public decimal Income { get; init; }
    public decimal Expenses { get; init; }
    public decimal Profit => Income - Expenses;
    public int OperationCount { get; init; }
    public int ExcludedCount { get; init; }
    public decimal AccruedTax { get; init; }
}

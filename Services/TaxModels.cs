using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public sealed class TaxSummaryDto
{
    public decimal UnpaidTotal { get; init; }
    public decimal PaidYearToDate { get; init; }
    public decimal PaidInTransactionsYearToDate { get; init; }
    public int OverdueCount { get; init; }
    public int UpcomingCount { get; init; }
    public DateTime? NextDueDate { get; init; }
    public string? NextDueName { get; init; }
    public decimal NextDueAmount { get; init; }
}

public sealed class TaxPageContextDto
{
    public TaxSummaryDto Summary { get; init; } = new();
    public TaxSystem? TaxSystem { get; init; }
    public TaxpayerKind TaxpayerKind { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string CompanyUnp { get; init; } = string.Empty;
}

public sealed record TaxHintDto(string Label, string Name, int DuePresetDays);

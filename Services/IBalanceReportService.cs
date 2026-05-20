namespace MiniFinance.Services;

public interface IBalanceReportService
{
    Task<BalanceReportDto> BuildAsync(string userId, ReportFilters filters);
}

public sealed class BalanceReportDto
{
    public decimal CashOnAccounts { get; init; }
    public decimal Receivables { get; init; }
    public decimal TotalAssets { get; init; }
    public decimal Payables { get; init; }
    public decimal TaxReserve { get; init; }
    public decimal TotalLiabilities { get; init; }
    public decimal NetWorth { get; init; }
}

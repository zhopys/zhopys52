namespace MiniFinance.Services;

public sealed class CategoryStatsDto
{
    public int Id { get; init; }
    public int TransactionCount { get; init; }
    public decimal TotalAmount { get; init; }
    public int MonthTransactionCount { get; init; }
    public decimal MonthAmount { get; init; }
}

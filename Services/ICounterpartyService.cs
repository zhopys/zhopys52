using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public interface ICounterpartyService
{
    Task<List<CounterpartyRecord>> ListAsync(string userId);
    Task<List<CounterpartyListItemDto>> ListWithStatsAsync(string userId);
    Task<CounterpartyRecord> CreateAsync(CounterpartyRecord record, string userId);
    Task<CounterpartyRecord> UpdateAsync(CounterpartyRecord record, string userId);
    Task DeleteAsync(int id, string userId);
    Task<CounterpartyDetailDto> GetDetailAsync(int id, string userId);
}

public sealed class CounterpartyListItemDto
{
    public CounterpartyRecord Record { get; init; } = new();
    public decimal TotalIncome { get; init; }
    public decimal TotalExpense { get; init; }
    public decimal NetBalance => TotalIncome - TotalExpense;
    public int TransactionCount { get; init; }
    public DateTime? LastTransactionDate { get; init; }
}

public sealed class CounterpartyCategoryStatDto
{
    public string Category { get; init; } = "";
    public decimal Income { get; init; }
    public decimal Expense { get; init; }
    public int Count { get; init; }
}

public sealed class CounterpartyDetailDto
{
    public CounterpartyRecord Record { get; init; } = new();
    public decimal TotalIncome { get; init; }
    public decimal TotalExpense { get; init; }
    public decimal NetBalance => TotalIncome - TotalExpense;
    public int TransactionCount { get; init; }
    public DateTime? FirstTransactionDate { get; init; }
    public DateTime? LastTransactionDate { get; init; }
    public decimal AvgTransactionAmount { get; init; }
    public IReadOnlyList<Transaction> Transactions { get; init; } = Array.Empty<Transaction>();
    public IReadOnlyList<CounterpartyCategoryStatDto> CategoryStats { get; init; } = Array.Empty<CounterpartyCategoryStatDto>();
    public IReadOnlyList<Debt> OpenDebts { get; init; } = Array.Empty<Debt>();
    public decimal OpenReceivable { get; init; }
    public decimal OpenPayable { get; init; }
}

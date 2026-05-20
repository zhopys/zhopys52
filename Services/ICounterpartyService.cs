using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public interface ICounterpartyService
{
    Task<List<CounterpartyRecord>> ListAsync(string userId);
    Task<CounterpartyRecord> CreateAsync(CounterpartyRecord record, string userId);
    Task<CounterpartyRecord> UpdateAsync(CounterpartyRecord record, string userId);
    Task DeleteAsync(int id, string userId);
    Task<CounterpartyDetailDto> GetDetailAsync(int id, string userId);
}

public sealed class CounterpartyDetailDto
{
    public CounterpartyRecord Record { get; init; } = new();
    public decimal TotalIncome { get; init; }
    public decimal TotalExpense { get; init; }
    public IReadOnlyList<Transaction> Transactions { get; init; } = Array.Empty<Transaction>();
}

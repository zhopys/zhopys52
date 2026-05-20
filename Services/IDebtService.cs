using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public interface IDebtService
{
    Task<List<Debt>> ListAsync(string userId);
    Task<DebtSummaryDto> GetSummaryAsync(string userId);
    Task<Debt> CreateAsync(Debt debt, string userId);
    Task<Debt> RecordPaymentAsync(int id, decimal amount, string userId);
    Task DeleteAsync(int id, string userId);
}

public sealed class DebtSummaryDto
{
    public decimal TotalReceivable { get; init; }
    public decimal TotalPayable { get; init; }
    public decimal NetPosition { get; init; }
}

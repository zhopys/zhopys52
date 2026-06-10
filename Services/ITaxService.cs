using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public interface ITaxService
{
    Task<List<TaxPayment>> GetTaxesAsync(string userId);
    Task<TaxPageContextDto> GetPageContextAsync(string userId);
    Task MarkAsPaidAsync(int taxId, string userId);
    Task MarkPartialPaidAsync(int taxId, decimal amount, string? receiptNote, string userId);
    Task AddTaxAsync(TaxPayment tax);
    Task UpdateTaxAsync(TaxPayment tax, string userId);
    Task DeleteTaxAsync(int taxId, string userId);
    Task<TaxPayment?> GetBySourceTransactionAsync(string userId, int transactionId);
}

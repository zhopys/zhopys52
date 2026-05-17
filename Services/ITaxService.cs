using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public interface ITaxService
    {
        Task<List<TaxPayment>> GetTaxesAsync(string userId);
        Task MarkAsPaidAsync(int taxId, string userId);
        Task AddTaxAsync(TaxPayment tax);
        Task DeleteTaxAsync(int taxId, string userId);
    }
}

using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public class TaxService : ITaxService
    {
        private readonly ApplicationDbContext _db;

        public TaxService(ApplicationDbContext db) => _db = db;

        public async Task<List<TaxPayment>> GetTaxesAsync(string userId)
        {
            return await _db.TaxPayments
                .Where(t => t.UserId == userId)
                .OrderBy(t => t.DueDate)
                .ToListAsync();
        }

        public async Task MarkAsPaidAsync(int taxId, string userId)
        {
            var tax = await _db.TaxPayments.FirstOrDefaultAsync(t => t.Id == taxId && t.UserId == userId);
            if (tax == null) return;

            tax.IsPaid = true;
            tax.PaidDate = DateTime.Today;

            // Create a corresponding expense transaction
            var transaction = new Transaction
            {
                Date = DateTime.Today,
                Amount = -tax.Amount,
                Description = $"Налог: {tax.Name}",
                Category = "Налоги",
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                IsMandatory = true
            };
            _db.Transactions.Add(transaction);
            await _db.SaveChangesAsync();
        }

        public async Task AddTaxAsync(TaxPayment tax)
        {
            tax.CreatedAt = DateTime.UtcNow;
            _db.TaxPayments.Add(tax);
            await _db.SaveChangesAsync();
        }

        public async Task DeleteTaxAsync(int taxId, string userId)
        {
            var tax = await _db.TaxPayments.FirstOrDefaultAsync(t => t.Id == taxId && t.UserId == userId);
            if (tax == null) return;

            _db.TaxPayments.Remove(tax);
            await _db.SaveChangesAsync();
        }
    }
}

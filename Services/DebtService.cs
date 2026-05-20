using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class DebtService : IDebtService
{
    private readonly ApplicationDbContext _db;

    public DebtService(ApplicationDbContext db) => _db = db;

    public Task<List<Debt>> ListAsync(string userId) =>
        _db.Debts.Where(d => d.UserId == userId && !d.IsSettled)
            .OrderBy(d => d.DueDate)
            .ToListAsync();

    public async Task<DebtSummaryDto> GetSummaryAsync(string userId)
    {
        var open = await _db.Debts.Where(d => d.UserId == userId && !d.IsSettled).ToListAsync();
        var recv = open.Where(d => d.Type == DebtType.Receivable).Sum(d => d.Amount - d.PaidAmount);
        var pay = open.Where(d => d.Type == DebtType.Payable).Sum(d => d.Amount - d.PaidAmount);
        return new DebtSummaryDto
        {
            TotalReceivable = recv,
            TotalPayable = pay,
            NetPosition = recv - pay
        };
    }

    public async Task<Debt> CreateAsync(Debt debt, string userId)
    {
        debt.UserId = userId;
        debt.CreatedAt = DateTime.UtcNow;
        _db.Debts.Add(debt);
        await _db.SaveChangesAsync();
        return debt;
    }

    public async Task<Debt> RecordPaymentAsync(int id, decimal amount, string userId)
    {
        var debt = await _db.Debts.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId)
            ?? throw new KeyNotFoundException();
        debt.PaidAmount += amount;
        if (debt.PaidAmount >= debt.Amount)
        {
            debt.PaidAmount = debt.Amount;
            debt.IsSettled = true;
        }
        await _db.SaveChangesAsync();
        return debt;
    }

    public async Task DeleteAsync(int id, string userId)
    {
        var d = await _db.Debts.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (d == null) return;
        _db.Debts.Remove(d);
        await _db.SaveChangesAsync();
    }
}

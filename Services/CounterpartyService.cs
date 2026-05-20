using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class CounterpartyService : ICounterpartyService
{
    private readonly ApplicationDbContext _db;

    public CounterpartyService(ApplicationDbContext db) => _db = db;

    public Task<List<CounterpartyRecord>> ListAsync(string userId) =>
        _db.Counterparties.Where(c => c.UserId == userId).OrderBy(c => c.Name).ToListAsync();

    public async Task<CounterpartyRecord> CreateAsync(CounterpartyRecord record, string userId)
    {
        record.UserId = userId;
        record.Name = record.Name.Trim();
        record.CreatedAt = DateTime.UtcNow;
        _db.Counterparties.Add(record);
        await _db.SaveChangesAsync();
        return record;
    }

    public async Task<CounterpartyRecord> UpdateAsync(CounterpartyRecord record, string userId)
    {
        var existing = await _db.Counterparties.FirstOrDefaultAsync(c => c.Id == record.Id && c.UserId == userId);
        if (existing == null) throw new KeyNotFoundException();

        existing.Name = record.Name.Trim();
        existing.Type = record.Type;
        existing.ContactPerson = record.ContactPerson;
        existing.Email = record.Email;
        existing.Phone = record.Phone;
        existing.TaxId = record.TaxId;
        existing.Notes = record.Notes;
        await _db.SaveChangesAsync();
        return existing;
    }

    public async Task DeleteAsync(int id, string userId)
    {
        var c = await _db.Counterparties.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (c == null) return;
        _db.Counterparties.Remove(c);
        await _db.SaveChangesAsync();
    }

    public async Task<CounterpartyDetailDto> GetDetailAsync(int id, string userId)
    {
        var record = await _db.Counterparties.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId)
            ?? throw new KeyNotFoundException();

        var txs = await _db.Transactions
            .Where(t => t.UserId == userId &&
                (t.CounterpartyId == id || (t.Counterparty != null && t.Counterparty == record.Name)))
            .OrderByDescending(t => t.Date)
            .Take(200)
            .ToListAsync();

        return new CounterpartyDetailDto
        {
            Record = record,
            TotalIncome = txs.Where(t => t.Amount > 0).Sum(t => t.Amount),
            TotalExpense = Math.Abs(txs.Where(t => t.Amount < 0).Sum(t => t.Amount)),
            Transactions = txs
        };
    }
}

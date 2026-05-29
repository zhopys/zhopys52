using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

/// <summary>Кэш контрагентов на время пакетного импорта.</summary>
public sealed class CounterpartyImportCache
{
    private readonly List<CounterpartyRecord> _records;
    private readonly string _userId;

    private CounterpartyImportCache(List<CounterpartyRecord> records, string userId)
    {
        _records = records;
        _userId = userId;
    }

    public static async Task<CounterpartyImportCache> LoadAsync(ApplicationDbContext db, string userId)
    {
        var list = await db.Counterparties.AsNoTracking()
            .Where(c => c.UserId == userId)
            .ToListAsync();
        return new CounterpartyImportCache(list, userId);
    }

    public void ApplyToTransaction(ApplicationDbContext db, Transaction transaction)
    {
        if (string.IsNullOrWhiteSpace(transaction.Counterparty))
        {
            transaction.Counterparty = null;
            transaction.CounterpartyId = null;
            return;
        }

        var canonical = CounterpartyNameMatcher.CanonicalDisplayName(transaction.Counterparty);
        var match = CounterpartyNameMatcher.FindBestMatch(canonical, _records);

        if (match != null)
        {
            transaction.Counterparty = match.Name;
            transaction.CounterpartyId = match.Id;
            return;
        }

        var created = new CounterpartyRecord
        {
            UserId = _userId,
            Name = canonical,
            Type = transaction.Amount >= 0 ? CounterpartyType.Client : CounterpartyType.Supplier,
            CreatedAt = DateTime.UtcNow
        };
        db.Counterparties.Add(created);
        _records.Add(created);
        transaction.Counterparty = created.Name;
        transaction.CounterpartyEntity = created;
    }
}

using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class CounterpartyService : ICounterpartyService
{
    private readonly ApplicationDbContext _db;
    private readonly IDataScopeService _dataScope;

    public CounterpartyService(ApplicationDbContext db, IDataScopeService dataScope)
    {
        _db = db;
        _dataScope = dataScope;
    }

    public async Task<List<CounterpartyRecord>> ListAsync(string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        return await _db.Counterparties.Where(c => c.UserId == userId).OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<List<CounterpartyListItemDto>> ListWithStatsAsync(string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var records = await _db.Counterparties.Where(c => c.UserId == userId).OrderBy(c => c.Name).ToListAsync();
        if (records.Count == 0) return [];

        var ids = records.Select(c => c.Id).ToList();
        var nameToId = records.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);

        var txs = await _db.Transactions
            .Where(t => t.UserId == userId &&
                (t.CounterpartyId != null && ids.Contains(t.CounterpartyId.Value) ||
                 (t.Counterparty != null && nameToId.ContainsKey(t.Counterparty))))
            .Select(t => new { t.CounterpartyId, t.Counterparty, t.Amount, t.Date })
            .ToListAsync();

        var statsById = records.ToDictionary(c => c.Id, _ => new TxAgg());

        foreach (var t in txs)
        {
            int? key = t.CounterpartyId;
            if (key == null && t.Counterparty != null && nameToId.TryGetValue(t.Counterparty, out var byName))
                key = byName;
            if (key == null || !statsById.TryGetValue(key.Value, out var agg)) continue;
            agg.Count++;
            if (t.Amount > 0) agg.Income += t.Amount;
            else agg.Expense += Math.Abs(t.Amount);
            if (agg.Last == null || t.Date > agg.Last) agg.Last = t.Date;
        }

        return records.Select(c =>
        {
            var s = statsById[c.Id];
            return new CounterpartyListItemDto
            {
                Record = c,
                TotalIncome = s.Income,
                TotalExpense = s.Expense,
                TransactionCount = s.Count,
                LastTransactionDate = s.Last
            };
        }).ToList();
    }

    private sealed class TxAgg
    {
        public decimal Income;
        public decimal Expense;
        public int Count;
        public DateTime? Last;
    }

    public async Task<CounterpartyRecord> CreateAsync(CounterpartyRecord record, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        record.UserId = userId;
        record.Name = record.Name.Trim();
        record.LogoUrl = NormalizeLogoUrl(record.LogoUrl);
        record.CreatedAt = DateTime.UtcNow;
        _db.Counterparties.Add(record);
        await _db.SaveChangesAsync();
        return record;
    }

    public async Task<CounterpartyRecord> UpdateAsync(CounterpartyRecord record, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var existing = await _db.Counterparties.FirstOrDefaultAsync(c => c.Id == record.Id && c.UserId == userId);
        if (existing == null) throw new KeyNotFoundException();

        var oldName = existing.Name;
        existing.Name = record.Name.Trim();
        existing.Type = record.Type;
        existing.ContactPerson = record.ContactPerson;
        existing.Email = record.Email;
        existing.Phone = record.Phone;
        existing.TaxId = record.TaxId;
        existing.Notes = record.Notes;
        existing.LogoUrl = NormalizeLogoUrl(record.LogoUrl);

        if (!string.Equals(oldName, existing.Name, StringComparison.Ordinal))
            await EntityLinkageHelper.SyncCounterpartyNameAsync(_db, existing.Id, existing.Name);

        await _db.SaveChangesAsync();
        return existing;
    }

    public async Task DeleteAsync(int id, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var c = await _db.Counterparties.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (c == null) return;

        await EntityLinkageHelper.UnlinkCounterpartyAsync(_db, id);
        _db.Counterparties.Remove(c);
        await _db.SaveChangesAsync();
    }

    public async Task<CounterpartyDetailDto> GetDetailAsync(int id, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var record = await _db.Counterparties.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId)
            ?? throw new KeyNotFoundException();

        var txs = await _db.Transactions
            .Where(t => t.UserId == userId &&
                (t.CounterpartyId == id || (t.Counterparty != null && t.Counterparty == record.Name)))
            .OrderByDescending(t => t.Date)
            .Take(200)
            .ToListAsync();

        var debts = await _db.Debts
            .Where(d => d.UserId == userId && !d.IsSettled &&
                (d.CounterpartyId == id || d.CounterpartyName == record.Name))
            .OrderBy(d => d.DueDate)
            .ToListAsync();

        var categoryStats = txs
            .GroupBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CounterpartyCategoryStatDto
            {
                Category = g.Key,
                Income = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                Expense = Math.Abs(g.Where(t => t.Amount < 0).Sum(t => t.Amount)),
                Count = g.Count()
            })
            .OrderByDescending(c => c.Income + c.Expense)
            .ToList();

        return new CounterpartyDetailDto
        {
            Record = record,
            TotalIncome = txs.Where(t => t.Amount > 0).Sum(t => t.Amount),
            TotalExpense = Math.Abs(txs.Where(t => t.Amount < 0).Sum(t => t.Amount)),
            TransactionCount = txs.Count,
            FirstTransactionDate = txs.Count > 0 ? txs.Min(t => t.Date) : null,
            LastTransactionDate = txs.Count > 0 ? txs.Max(t => t.Date) : null,
            AvgTransactionAmount = txs.Count > 0 ? txs.Average(t => Math.Abs(t.Amount)) : 0,
            CategoryStats = categoryStats,
            Transactions = txs,
            OpenDebts = debts,
            OpenReceivable = debts.Where(d => d.Type == DebtType.Receivable).Sum(d => d.Amount - d.PaidAmount),
            OpenPayable = debts.Where(d => d.Type == DebtType.Payable).Sum(d => d.Amount - d.PaidAmount)
        };
    }

    public static string GetDisplayLogoUrl(CounterpartyRecord record) =>
        !string.IsNullOrWhiteSpace(record.LogoUrl)
            ? record.LogoUrl.Trim()
            : $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(record.Name)}&background=7c3aed&color=fff&size=64&bold=true";

    private static string? NormalizeLogoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var t = url.Trim();
        return t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? t
            : null;
    }
}

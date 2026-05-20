using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class TagService : ITagService
{
    private readonly ApplicationDbContext _db;

    public TagService(ApplicationDbContext db) => _db = db;

    public Task<List<Tag>> ListAsync(string userId) =>
        _db.Tags.Where(t => t.UserId == userId).OrderBy(t => t.Name).ToListAsync();

    public async Task<Tag> CreateAsync(string userId, string name, string? color = null)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Имя тега обязательно.");

        var existing = await _db.Tags.FirstOrDefaultAsync(t => t.UserId == userId && t.Name == name);
        if (existing != null) return existing;

        var tag = new Tag { UserId = userId, Name = name, Color = color ?? "#7c3aed" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();
        return tag;
    }

    public async Task SetTransactionTagsAsync(int transactionId, string userId, IEnumerable<string> tagNames)
    {
        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId);
        if (tx == null) throw new KeyNotFoundException("Транзакция не найдена.");

        var names = tagNames.Select(n => n.Trim()).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existingLinks = await _db.TransactionTags.Where(tt => tt.TransactionId == transactionId).ToListAsync();
        _db.TransactionTags.RemoveRange(existingLinks);

        foreach (var name in names)
        {
            var tag = await CreateAsync(userId, name);
            _db.TransactionTags.Add(new TransactionTag { TransactionId = transactionId, TagId = tag.Id });
        }

        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<string>> GetTransactionTagNamesAsync(int transactionId, string userId)
    {
        var ok = await _db.Transactions.AnyAsync(t => t.Id == transactionId && t.UserId == userId);
        if (!ok) return Array.Empty<string>();

        return await _db.TransactionTags
            .Where(tt => tt.TransactionId == transactionId)
            .Include(tt => tt.Tag)
            .Select(tt => tt.Tag.Name)
            .ToListAsync();
    }
}

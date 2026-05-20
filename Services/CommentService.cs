using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class CommentService : ICommentService
{
    private readonly ApplicationDbContext _db;

    public CommentService(ApplicationDbContext db) => _db = db;

    public async Task<List<TransactionComment>> ListAsync(int transactionId, string userId)
    {
        var ok = await _db.Transactions.AnyAsync(t => t.Id == transactionId && t.UserId == userId);
        if (!ok) return new List<TransactionComment>();

        return await _db.TransactionComments
            .Where(c => c.TransactionId == transactionId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<TransactionComment> AddAsync(int transactionId, string userId, string text, string? authorName)
    {
        var ok = await _db.Transactions.AnyAsync(t => t.Id == transactionId && t.UserId == userId);
        if (!ok) throw new KeyNotFoundException();

        var comment = new TransactionComment
        {
            TransactionId = transactionId,
            UserId = userId,
            Text = text.Trim(),
            AuthorName = authorName,
            CreatedAt = DateTime.UtcNow
        };
        _db.TransactionComments.Add(comment);
        await _db.SaveChangesAsync();
        return comment;
    }
}

using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class AttachmentService : IAttachmentService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    private const long MaxFileSize = 10 * 1024 * 1024;

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    public AttachmentService(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public async Task<TransactionAttachment> UploadAsync(int transactionId, string userId, Stream fileStream, string fileName, string contentType)
    {
        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId);
        if (tx == null) throw new KeyNotFoundException("Транзакция не найдена.");

        var ext = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException("Допустимы файлы PDF, JPG, PNG, WEBP.");

        if (fileStream.Length > MaxFileSize)
            throw new InvalidOperationException("Максимальный размер файла — 10 МБ.");

        var safeName = $"{Guid.NewGuid():N}{ext}";
        var dir = Path.Combine(_env.WebRootPath, "uploads", userId, transactionId.ToString());
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, safeName);

        await using (var fs = File.Create(fullPath))
            await fileStream.CopyToAsync(fs);

        var relative = $"/uploads/{userId}/{transactionId}/{safeName}";
        var attachment = new TransactionAttachment
        {
            TransactionId = transactionId,
            UserId = userId,
            FileName = Path.GetFileName(fileName),
            StoredPath = relative,
            ContentType = contentType,
            FileSize = fileStream.Length
        };

        _db.TransactionAttachments.Add(attachment);
        await _db.SaveChangesAsync();
        return attachment;
    }

    public Task<List<TransactionAttachment>> ListAsync(int transactionId, string userId) =>
        _db.TransactionAttachments
            .Where(a => a.TransactionId == transactionId && a.UserId == userId)
            .OrderByDescending(a => a.UploadedAt)
            .ToListAsync();

    public async Task DeleteAsync(int attachmentId, string userId)
    {
        var a = await _db.TransactionAttachments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.UserId == userId);
        if (a == null) return;

        var physical = Path.Combine(_env.WebRootPath, a.StoredPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(physical)) File.Delete(physical);

        _db.TransactionAttachments.Remove(a);
        await _db.SaveChangesAsync();
    }

    public string GetPublicUrl(TransactionAttachment attachment) => attachment.StoredPath;
}

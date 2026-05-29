using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;

namespace MiniFinance.Services;

public class AccountProfileService : IAccountProfileService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;

    public AccountProfileService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env)
    {
        _db = db;
        _userManager = userManager;
        _env = env;
    }

    public async Task<AccountDataSummaryDto> GetDataSummaryAsync(string userId)
    {
        var txs = await _db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => t.Amount)
            .ToListAsync();

        var txIds = await _db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => t.Id)
            .ToListAsync();

        return new AccountDataSummaryDto
        {
            Transactions = txs.Count,
            Balance = txs.Sum(),
            Projects = await _db.Projects.CountAsync(p => p.UserId == userId),
            Reminders = await _db.Reminders.CountAsync(r => r.UserId == userId),
            TaxPayments = await _db.TaxPayments.CountAsync(t => t.UserId == userId),
            Counterparties = await _db.Counterparties.CountAsync(c => c.UserId == userId),
            Debts = await _db.Debts.CountAsync(d => d.UserId == userId),
            Tags = await _db.Tags.CountAsync(t => t.UserId == userId),
            Attachments = txIds.Count == 0
                ? 0
                : await _db.TransactionAttachments.CountAsync(a => txIds.Contains(a.TransactionId))
        };
    }

    public async Task<byte[]> BuildExportJsonAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId)
                   ?? throw new InvalidOperationException("Пользователь не найден");

        var transactions = await _db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Date)
            .ToListAsync();

        var categoryNames = transactions.Select(t => t.Category).Distinct().ToList();
        var categories = await _db.Categories.AsNoTracking()
            .Where(c => categoryNames.Contains(c.Name))
            .ToListAsync();

        var payload = new
        {
            exportedAt = DateTime.UtcNow,
            user = new
            {
                user.Email,
                user.UserName,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.BaseCurrency,
                user.EnableNotifications,
                user.NotifyTaxes,
                user.NotifyBills,
                user.NotifyCashGaps,
                user.NotificationDaysBefore,
                user.CreatedAt
            },
            transactions,
            categories,
            projects = await _db.Projects.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(),
            reminders = await _db.Reminders.AsNoTracking().Where(r => r.UserId == userId).ToListAsync(),
            taxes = await _db.TaxPayments.AsNoTracking().Where(t => t.UserId == userId).ToListAsync(),
            taxAutoRules = await _db.TaxAutoRules.AsNoTracking().Where(r => r.UserId == userId).ToListAsync(),
            counterparties = await _db.Counterparties.AsNoTracking().Where(c => c.UserId == userId).ToListAsync(),
            debts = await _db.Debts.AsNoTracking().Where(d => d.UserId == userId).ToListAsync(),
            tags = await _db.Tags.AsNoTracking().Where(t => t.UserId == userId).ToListAsync(),
            organization = await _db.OrganizationSettings.AsNoTracking()
                .FirstOrDefaultAsync(o => o.UserId == userId)
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        return Encoding.UTF8.GetBytes(json);
    }

    public async Task<byte[]> BuildExportCsvAsync(string userId)
    {
        var txs = await _db.Transactions.AsNoTracking()
            .Include(t => t.Project)
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.Id)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Date,Amount,Description,Category,Project,Counterparty,PaymentMethod");
        foreach (var t in txs)
        {
            sb.AppendLine(string.Join(",",
                t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                t.Amount.ToString(CultureInfo.InvariantCulture),
                CsvEscape(t.Description),
                CsvEscape(t.Category),
                CsvEscape(t.Project?.Name),
                CsvEscape(t.Counterparty),
                CsvEscape(t.PaymentMethod?.ToString())));
        }

        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(body, 0, result, bom.Length, body.Length);
        return result;
    }

    public async Task<(bool Success, string? Error)> DeleteAccountAsync(string userId, string password)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return (false, "Пользователь не найден");

        if (!await _userManager.CheckPasswordAsync(user, password))
            return (false, "Неверный пароль");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var txIds = await _db.Transactions.Where(t => t.UserId == userId).Select(t => t.Id).ToListAsync();
            if (txIds.Count > 0)
            {
                var attachmentPaths = await _db.TransactionAttachments
                    .Where(a => txIds.Contains(a.TransactionId))
                    .Select(a => a.StoredPath)
                    .ToListAsync();
                foreach (var path in attachmentPaths)
                    DeletePhysicalFile(path);

                await _db.TransactionComments.Where(c => txIds.Contains(c.TransactionId)).ExecuteDeleteAsync();
                await _db.TransactionAttachments.Where(a => txIds.Contains(a.TransactionId)).ExecuteDeleteAsync();
                await _db.TransactionTags.Where(tt => txIds.Contains(tt.TransactionId)).ExecuteDeleteAsync();
            }

            await _db.Transactions.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await _db.TransactionImportBatches.Where(b => b.UserId == userId).ExecuteDeleteAsync();
            await _db.Reminders.Where(r => r.UserId == userId).ExecuteDeleteAsync();
            await _db.TaxPayments.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await _db.TaxAutoRules.Where(r => r.UserId == userId).ExecuteDeleteAsync();
            await _db.Debts.Where(d => d.UserId == userId).ExecuteDeleteAsync();
            await _db.Counterparties.Where(c => c.UserId == userId).ExecuteDeleteAsync();
            await _db.Projects.Where(p => p.UserId == userId).ExecuteDeleteAsync();
            await _db.Tags.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await _db.OrganizationSettings.Where(o => o.UserId == userId).ExecuteDeleteAsync();

            await _db.Users
                .Where(u => u.WorkspaceOwnerUserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.WorkspaceOwnerUserId, (string?)null));

            _db.ChangeTracker.Clear();

            var freshUser = await _userManager.FindByIdAsync(userId);
            if (freshUser == null)
            {
                await transaction.RollbackAsync();
                return (false, "Пользователь не найден");
            }

            var deleteResult = await _userManager.DeleteAsync(freshUser);
            if (!deleteResult.Succeeded)
            {
                await transaction.RollbackAsync();
                return (false, IdentityErrorTranslator.Join(deleteResult.Errors));
            }

            await transaction.CommitAsync();

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", userId);
            if (Directory.Exists(uploadsDir))
            {
                try { Directory.Delete(uploadsDir, recursive: true); } catch { /* ignore */ }
            }

            return (true, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            _db.ChangeTracker.Clear();
            return (false, "Не удалось удалить аккаунт из‑за конфликта данных. Обновите страницу и повторите.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return (false, ex.Message);
        }
    }

    private void DeletePhysicalFile(string storedPath)
    {
        var physical = Path.Combine(
            _env.WebRootPath,
            storedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(physical))
            File.Delete(physical);
    }

    private static string CsvEscape(string? s)
    {
        var v = s ?? "";
        return v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? $"\"{v.Replace("\"", "\"\"")}\""
            : v;
    }
}

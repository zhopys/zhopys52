using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;

namespace MiniFinance.Services;

public class TransactionDataStatusService : ITransactionDataStatusService
{
    private readonly ApplicationDbContext _db;

    public TransactionDataStatusService(ApplicationDbContext db) => _db = db;

    public async Task<DataStatusDto> GetStatusAsync(string userId, DateTime? periodStart = null, DateTime? periodEnd = null)
    {
        var total = await _db.Transactions.CountAsync(t => t.UserId == userId);

        var lastTx = await _db.Transactions
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt ?? t.Date)
            .Select(t => new { t.CreatedAt, t.Date })
            .FirstOrDefaultAsync();

        var lastAt = lastTx == null
            ? (DateTime?)null
            : lastTx.CreatedAt ?? lastTx.Date;

        int periodCount = 0;
        if (periodStart.HasValue && periodEnd.HasValue)
        {
            periodCount = await _db.Transactions.CountAsync(t =>
                t.UserId == userId && t.Date >= periodStart && t.Date <= periodEnd);
        }

        return new DataStatusDto
        {
            HasData = total > 0,
            TotalTransactions = total,
            PeriodTransactions = periodCount,
            LastUpdatedAt = lastAt,
            SourceLabel = "CSV-импорт и ручной ввод"
        };
    }
}

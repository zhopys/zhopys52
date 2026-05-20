using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;

namespace MiniFinance.Services;

public class BalanceReportService : IBalanceReportService
{
    private readonly ApplicationDbContext _db;

    public BalanceReportService(ApplicationDbContext db) => _db = db;

    public async Task<BalanceReportDto> BuildAsync(string userId, ReportFilters filters)
    {
        var txs = await _db.Transactions
            .Where(t => t.UserId == userId && t.Date <= filters.End)
            .ToListAsync();

        var cash = txs.Sum(t => t.Amount);

        var debts = await _db.Debts.Where(d => d.UserId == userId && !d.IsSettled).ToListAsync();
        var receivables = debts.Where(d => d.Type == Data.Models.DebtType.Receivable).Sum(d => d.Amount - d.PaidAmount);
        var payables = debts.Where(d => d.Type == Data.Models.DebtType.Payable).Sum(d => d.Amount - d.PaidAmount);

        var taxReserve = Math.Abs(txs
            .Where(t => t.Date >= filters.Start && t.Date <= filters.End &&
                        CategoryBucketHelper.IsTax(t.Category) && t.Amount < 0)
            .Sum(t => t.Amount));

        var assets = cash + receivables;
        var liabilities = payables + taxReserve;

        return new BalanceReportDto
        {
            CashOnAccounts = cash,
            Receivables = receivables,
            TotalAssets = assets,
            Payables = payables,
            TaxReserve = taxReserve,
            TotalLiabilities = liabilities,
            NetWorth = assets - liabilities
        };
    }
}

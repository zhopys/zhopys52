using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class DebtService : IDebtService
{
    private readonly ApplicationDbContext _db;
    private readonly ITransactionService _transactionService;
    private readonly IDataScopeService _dataScope;

    public DebtService(ApplicationDbContext db, ITransactionService transactionService, IDataScopeService dataScope)
    {
        _db = db;
        _transactionService = transactionService;
        _dataScope = dataScope;
    }

    public async Task<List<Debt>> ListAsync(string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        return await _db.Debts.Where(d => d.UserId == userId && !d.IsSettled)
            .OrderBy(d => d.DueDate)
            .ToListAsync();
    }

    public async Task<DebtSummaryDto> GetSummaryAsync(string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
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
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("Пользователь не определён.", nameof(userId));

        if (debt.Amount <= 0)
            throw new InvalidOperationException("Сумма долга должна быть больше нуля.");

        debt.UserId = userId;
        debt.CreatedAt = DateTime.UtcNow;
        debt.DueDate = debt.DueDate?.Date;

        await EntityLinkageHelper.ApplyCounterpartyToDebtAsync(_db, debt, userId);

        _db.Debts.Add(debt);
        await _db.SaveChangesAsync();
        return debt;
    }

    public async Task<Debt> RecordPaymentAsync(int id, decimal amount, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        if (amount <= 0)
            throw new InvalidOperationException("Сумма погашения должна быть больше нуля.");

        var debt = await _db.Debts.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && !d.IsSettled)
            ?? throw new InvalidOperationException("Долг не найден или уже погашен.");

        var remaining = debt.Amount - debt.PaidAmount;
        if (remaining <= 0.01m)
            throw new InvalidOperationException("Долг уже полностью погашен.");

        var pay = Math.Min(amount, remaining);
        debt.PaidAmount += pay;

        var isReceivable = debt.Type == DebtType.Receivable;
        await _transactionService.CreateAsync(new Transaction
        {
            Date = DateTime.Today,
            Amount = isReceivable ? pay : -pay,
            Description = $"{(isReceivable ? "Поступление по долгу" : "Погашение долга")}: {debt.CounterpartyName}",
            Category = isReceivable ? CategoryDefaults.DefaultIncome : CategoryDefaults.DefaultExpense,
            CounterpartyId = debt.CounterpartyId,
            Counterparty = debt.CounterpartyName,
            IsConfirmed = true
        }, userId);

        if (debt.PaidAmount >= debt.Amount - 0.01m)
        {
            debt.PaidAmount = debt.Amount;
            debt.IsSettled = true;
        }

        await _db.SaveChangesAsync();
        return debt;
    }

    public async Task<DebtDetailDto> GetDetailAsync(int id, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var debt = await _db.Debts.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId)
            ?? throw new KeyNotFoundException();

        var name = debt.CounterpartyName;
        var txs = await _db.Transactions
            .Where(t => t.UserId == userId &&
                        (t.CounterpartyId == debt.CounterpartyId ||
                         (t.Counterparty != null && t.Counterparty == name)) &&
                        (EF.Functions.Like(t.Description, "%долг%") ||
                         EF.Functions.Like(t.Description, "%Долг%") ||
                         EF.Functions.Like(t.Description, "%поступление%") ||
                         EF.Functions.Like(t.Description, "%Погашение%")))
            .OrderByDescending(t => t.Date)
            .Take(50)
            .ToListAsync();

        var remaining = Math.Max(0, debt.Amount - debt.PaidAmount);
        var progress = debt.Amount > 0 ? (int)Math.Min(100, Math.Round(debt.PaidAmount / debt.Amount * 100)) : 0;

        return new DebtDetailDto
        {
            Debt = debt,
            Remaining = remaining,
            ProgressPercent = progress,
            RelatedTransactions = txs
        };
    }

    public async Task DeleteAsync(int id, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var d = await _db.Debts.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (d == null) return;
        _db.Debts.Remove(d);
        await _db.SaveChangesAsync();
    }
}

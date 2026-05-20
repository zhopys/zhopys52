using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

/// <summary>
/// Связывает сущности по FK и справочникам при создании/обновлении записей.
/// </summary>
public static class EntityLinkageHelper
{
    public static async Task ApplyCounterpartyToTransactionAsync(
        ApplicationDbContext db,
        Transaction transaction,
        string userId)
    {
        if (transaction.CounterpartyId.HasValue)
        {
            var byId = await db.Counterparties
                .FirstOrDefaultAsync(c => c.Id == transaction.CounterpartyId.Value && c.UserId == userId);
            if (byId != null)
            {
                transaction.CounterpartyId = byId.Id;
                transaction.Counterparty = byId.Name;
                return;
            }

            transaction.CounterpartyId = null;
        }

        if (string.IsNullOrWhiteSpace(transaction.Counterparty))
        {
            transaction.Counterparty = null;
            transaction.CounterpartyId = null;
            return;
        }

        var name = transaction.Counterparty.Trim();
        var existing = await db.Counterparties
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Name == name);

        if (existing == null)
        {
            existing = new CounterpartyRecord
            {
                UserId = userId,
                Name = name,
                Type = transaction.Amount >= 0 ? CounterpartyType.Client : CounterpartyType.Supplier,
                CreatedAt = DateTime.UtcNow
            };
            db.Counterparties.Add(existing);
            await db.SaveChangesAsync();
        }

        transaction.CounterpartyId = existing.Id;
        transaction.Counterparty = existing.Name;
    }

    public static async Task ApplyCounterpartyToDebtAsync(
        ApplicationDbContext db,
        Debt debt,
        string userId)
    {
        if (debt.CounterpartyId.HasValue)
        {
            var byId = await db.Counterparties
                .FirstOrDefaultAsync(c => c.Id == debt.CounterpartyId.Value && c.UserId == userId);
            if (byId != null)
            {
                debt.CounterpartyId = byId.Id;
                debt.CounterpartyName = byId.Name;
                return;
            }

            debt.CounterpartyId = null;
        }

        if (string.IsNullOrWhiteSpace(debt.CounterpartyName))
            throw new InvalidOperationException("Укажите контрагента.");

        var name = debt.CounterpartyName.Trim();
        var existing = await db.Counterparties
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Name == name);

        if (existing == null)
        {
            existing = new CounterpartyRecord
            {
                UserId = userId,
                Name = name,
                Type = debt.Type == DebtType.Receivable ? CounterpartyType.Client : CounterpartyType.Supplier,
                CreatedAt = DateTime.UtcNow
            };
            db.Counterparties.Add(existing);
            await db.SaveChangesAsync();
        }

        debt.CounterpartyId = existing.Id;
        debt.CounterpartyName = existing.Name;
    }

    public static async Task ValidateProjectAsync(ApplicationDbContext db, int? projectId, string userId)
    {
        if (!projectId.HasValue) return;

        var proj = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId.Value);
        if (proj == null)
            throw new TransactionValidationException("Указанный проект не найден.");

        if (!proj.IsDefault && proj.UserId != userId)
            throw new TransactionValidationException("Проект не принадлежит текущему пользователю.");
    }

    public static async Task SyncCounterpartyNameAsync(ApplicationDbContext db, int counterpartyId, string newName)
    {
        var txs = await db.Transactions.Where(t => t.CounterpartyId == counterpartyId).ToListAsync();
        foreach (var t in txs)
            t.Counterparty = newName;

        var debts = await db.Debts.Where(d => d.CounterpartyId == counterpartyId).ToListAsync();
        foreach (var d in debts)
            d.CounterpartyName = newName;
    }

    public static async Task UnlinkCounterpartyAsync(ApplicationDbContext db, int counterpartyId)
    {
        var txs = await db.Transactions.Where(t => t.CounterpartyId == counterpartyId).ToListAsync();
        foreach (var t in txs)
            t.CounterpartyId = null;

        var debts = await db.Debts.Where(d => d.CounterpartyId == counterpartyId).ToListAsync();
        foreach (var d in debts)
            d.CounterpartyId = null;
    }
}

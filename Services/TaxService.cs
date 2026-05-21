using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class TaxService : ITaxService
{
    private readonly ApplicationDbContext _db;
    private readonly ITransactionService _transactionService;
    private readonly IDataScopeService _dataScope;

    public TaxService(ApplicationDbContext db, ITransactionService transactionService, IDataScopeService dataScope)
    {
        _db = db;
        _transactionService = transactionService;
        _dataScope = dataScope;
    }

    public async Task<List<TaxPayment>> GetTaxesAsync(string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        return await _db.TaxPayments
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.IsPaid)
            .ThenBy(t => t.DueDate)
            .ToListAsync();
    }

    public async Task<TaxPageContextDto> GetPageContextAsync(string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var taxes = await GetTaxesAsync(userId);
        var today = DateTime.Today;

        var settings = await _db.OrganizationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId);

        var yearStart = GetFinancialYearStart(today, settings?.FinancialYearStartMonth ?? 1);

        var unpaid = taxes.Where(t => !t.IsPaid).ToList();
        var next = unpaid.Where(t => t.DueDate >= today).OrderBy(t => t.DueDate).FirstOrDefault()
                   ?? unpaid.OrderBy(t => t.DueDate).FirstOrDefault();

        var paidFromPlans = taxes
            .Where(t => t.IsPaid && t.PaidDate >= yearStart)
            .Sum(t => t.Amount);

        var paidFromTx = await _db.Transactions
            .Where(t => t.UserId == userId
                        && t.Date >= yearStart
                        && t.Amount < 0
                        && (t.Category == "Налоги" || EF.Functions.Like(t.Category, "%налог%")))
            .SumAsync(t => -t.Amount);

        return new TaxPageContextDto
        {
            TaxSystem = settings?.TaxSystem,
            CompanyName = settings?.CompanyName ?? string.Empty,
            CompanyUnp = settings?.UNP ?? string.Empty,
            Summary = new TaxSummaryDto
            {
                UnpaidTotal = unpaid.Sum(t => t.Amount - t.PaidAmount),
                PaidYearToDate = paidFromPlans,
                PaidInTransactionsYearToDate = paidFromTx,
                OverdueCount = unpaid.Count(t => t.DueDate < today),
                UpcomingCount = unpaid.Count(t => t.DueDate >= today),
                NextDueDate = next?.DueDate,
                NextDueName = next?.Name,
                NextDueAmount = next == null ? 0 : next.Amount - next.PaidAmount
            }
        };
    }

    private static DateTime GetFinancialYearStart(DateTime today, int startMonth)
    {
        startMonth = Math.Clamp(startMonth, 1, 12);
        var year = today.Year;
        if (today.Month < startMonth)
            year--;
        return new DateTime(year, startMonth, 1);
    }

    public async Task MarkAsPaidAsync(int taxId, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var tax = await _db.TaxPayments.FirstOrDefaultAsync(t => t.Id == taxId && t.UserId == userId);
        if (tax == null || tax.IsPaid) return;

        var remaining = tax.Amount - tax.PaidAmount;
        if (remaining > 0)
            await MarkPartialPaidAsync(taxId, remaining, tax.ReceiptNote, userId);
    }

    public async Task MarkPartialPaidAsync(int taxId, decimal amount, string? receiptNote, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        if (amount <= 0) throw new ArgumentException("Сумма должна быть больше нуля");

        var tax = await _db.TaxPayments.FirstOrDefaultAsync(t => t.Id == taxId && t.UserId == userId);
        if (tax == null || tax.IsPaid) return;

        var remaining = tax.Amount - tax.PaidAmount;
        var pay = Math.Min(amount, remaining);
        tax.PaidAmount += pay;
        if (!string.IsNullOrWhiteSpace(receiptNote))
            tax.ReceiptNote = receiptNote.Trim();

        await _transactionService.CreateAsync(new Transaction
        {
            Date = DateTime.Today,
            Amount = -pay,
            Description = $"Налог: {tax.Name}" + (tax.PaidAmount < tax.Amount ? " (частично)" : ""),
            Category = "Налоги",
            IsMandatory = true,
            Notes = receiptNote
        }, userId);

        if (tax.PaidAmount >= tax.Amount)
        {
            tax.IsPaid = true;
            tax.PaidDate = DateTime.Today;
        }

        await _db.SaveChangesAsync();
    }

    public async Task AddTaxAsync(TaxPayment tax)
    {
        tax.CreatedAt = DateTime.UtcNow;
        _db.TaxPayments.Add(tax);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateTaxAsync(TaxPayment tax, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var existing = await _db.TaxPayments.FirstOrDefaultAsync(t => t.Id == tax.Id && t.UserId == userId);
        if (existing == null || existing.IsPaid) return;

        existing.Name = tax.Name.Trim();
        existing.Amount = tax.Amount;
        existing.DueDate = tax.DueDate;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteTaxAsync(int taxId, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var tax = await _db.TaxPayments.FirstOrDefaultAsync(t => t.Id == taxId && t.UserId == userId);
        if (tax == null) return;

        _db.TaxPayments.Remove(tax);
        await _db.SaveChangesAsync();
    }
}

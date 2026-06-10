using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class TaxService : ITaxService
{
    private readonly ApplicationDbContext _db;
    private readonly ITaxFinanceSummaryService _financeSummary;
    private readonly IDataScopeService _dataScope;
    private readonly IUserContextService _userContext;

    public TaxService(
        ApplicationDbContext db,
        ITaxFinanceSummaryService financeSummary,
        IDataScopeService dataScope,
        IUserContextService userContext)
    {
        _db = db;
        _financeSummary = financeSummary;
        _dataScope = dataScope;
        _userContext = userContext;
    }

    private async Task EnsureTaxAccessAsync(string userId)
    {
        var ctx = await _userContext.GetContextAsync(userId);
        if (!_userContext.CanManageTaxes(ctx))
            throw new UnauthorizedAccessException(AccessDeniedMessages.ForPolicy(AuthorizationPolicies.CanManageTaxes));
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

        var paidFromLedger = await _financeSummary.GetTaxCategoryPaidYearToDateAsync(userId, yearStart);

        return new TaxPageContextDto
        {
            TaxSystem = settings?.TaxSystem,
            TaxpayerKind = settings?.TaxpayerKind ?? TaxpayerKind.LegalEntity,
            CompanyName = settings?.CompanyName ?? string.Empty,
            CompanyUnp = settings?.UNP ?? string.Empty,
            Summary = new TaxSummaryDto
            {
                UnpaidTotal = unpaid.Sum(t => t.Amount - t.PaidAmount),
                PaidYearToDate = paidFromPlans,
                PaidInTransactionsYearToDate = paidFromLedger,
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
        await EnsureTaxAccessAsync(userId);
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var tax = await _db.TaxPayments.FirstOrDefaultAsync(t => t.Id == taxId && t.UserId == userId);
        if (tax == null || tax.IsPaid) return;

        var remaining = tax.Amount - tax.PaidAmount;
        if (remaining > 0)
            await MarkPartialPaidAsync(taxId, remaining, tax.ReceiptNote, userId);
    }

    public async Task MarkPartialPaidAsync(int taxId, decimal amount, string? receiptNote, string userId)
    {
        await EnsureTaxAccessAsync(userId);
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);

        var tax = await _db.TaxPayments.FirstOrDefaultAsync(t => t.Id == taxId && t.UserId == userId);
        if (tax == null || tax.IsPaid) return;

        var remaining = tax.Amount - tax.PaidAmount;
        var validation = TaxFieldValidation.ValidatePartialPayment(amount, remaining, receiptNote);
        if (validation.HasErrors)
            throw new ArgumentException(validation.FirstError());

        var pay = Math.Min(amount, remaining);
        tax.PaidAmount += pay;
        if (!string.IsNullOrWhiteSpace(receiptNote))
            tax.ReceiptNote = receiptNote.Trim();

        _db.Transactions.Add(new Transaction
        {
            UserId = userId,
            Date = DateTime.Today,
            Amount = -pay,
            Description = $"Налог: {tax.Name}" + (tax.PaidAmount < tax.Amount ? " (частично)" : ""),
            Category = "Налоги",
            IsMandatory = true,
            IsConfirmed = true,
            ApprovalStatus = TransactionApprovalStatus.Approved,
            Notes = receiptNote,
            CreatedAt = DateTime.UtcNow
        });

        if (tax.PaidAmount >= tax.Amount)
        {
            tax.IsPaid = true;
            tax.PaidDate = DateTime.Today;
        }

        await _db.SaveChangesAsync();
    }

    public async Task AddTaxAsync(TaxPayment tax)
    {
        tax.UserId = await ServiceDataScope.ResolveAsync(_dataScope, tax.UserId);
        tax.Name = tax.Name.Trim();
        ValidateTaxEntity(tax);
        tax.CreatedAt = DateTime.UtcNow;
        _db.TaxPayments.Add(tax);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateTaxAsync(TaxPayment tax, string userId)
    {
        await EnsureTaxAccessAsync(userId);
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var existing = await _db.TaxPayments.FirstOrDefaultAsync(t => t.Id == tax.Id && t.UserId == userId);
        if (existing == null || existing.IsPaid) return;

        tax.Name = tax.Name.Trim();
        ValidateTaxEntity(tax);
        existing.Name = tax.Name;
        existing.Amount = tax.Amount;
        existing.DueDate = tax.DueDate;
        await _db.SaveChangesAsync();
    }

    private static void ValidateTaxEntity(TaxPayment tax)
    {
        if (string.IsNullOrWhiteSpace(tax.Name))
            throw new ArgumentException("Укажите название налогового платежа");
        if (tax.Name.Length > TaxFieldValidation.MaxTaxNameLength)
            throw new ArgumentException($"Название не длиннее {TaxFieldValidation.MaxTaxNameLength} символов");
        if (tax.Amount <= 0)
            throw new ArgumentException("Сумма должна быть больше нуля");
        if (tax.Amount > TaxFieldValidation.MaxAmount)
            throw new ArgumentException($"Сумма не может быть больше {TaxFieldValidation.MaxAmount:N0} BYN");
        if (tax.DueDate.Year < 2000 || tax.DueDate.Year > 2100)
            throw new ArgumentException("Укажите корректную дату срока уплаты");
    }

    public async Task DeleteTaxAsync(int taxId, string userId)
    {
        await EnsureTaxAccessAsync(userId);
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var tax = await _db.TaxPayments.FirstOrDefaultAsync(t => t.Id == taxId && t.UserId == userId);
        if (tax == null) return;

        _db.TaxPayments.Remove(tax);
        await _db.SaveChangesAsync();
    }

    public async Task<TaxPayment?> GetBySourceTransactionAsync(string userId, int transactionId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        return await _db.TaxPayments
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.SourceTransactionId == transactionId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();
    }
}

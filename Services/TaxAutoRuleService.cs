using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class TaxAutoRuleService : ITaxAutoRuleService
{
    private readonly ApplicationDbContext _db;
    private readonly ITaxService _taxService;
    private readonly ITaxFinanceSummaryService _financeSummary;
    private readonly IDataScopeService _dataScope;

    public TaxAutoRuleService(
        ApplicationDbContext db,
        ITaxService taxService,
        ITaxFinanceSummaryService financeSummary,
        IDataScopeService dataScope)
    {
        _db = db;
        _taxService = taxService;
        _financeSummary = financeSummary;
        _dataScope = dataScope;
    }

    public async Task<List<TaxAutoRule>> ListAsync(string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        return await _db.TaxAutoRules.Where(r => r.UserId == userId).OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync();
    }

    public async Task<TaxAutoRule> SaveAsync(TaxAutoRule rule, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        rule.UserId = userId;
        rule.Name = rule.Name.Trim();
        rule.PaymentName = rule.PaymentName.Trim();
        rule.Formula = rule.Formula.Trim();

        var test = TaxFormulaEvaluator.TryEvaluate(rule.Formula, new TaxFormulaContext { Income = 1000, Expenses = 200 });
        if (!test.Ok)
            throw new InvalidOperationException(test.Error ?? "Неверная формула");

        if (rule.Id == 0)
        {
            var maxOrder = await _db.TaxAutoRules.Where(r => r.UserId == userId).MaxAsync(r => (int?)r.SortOrder) ?? 0;
            rule.SortOrder = maxOrder + 1;
            _db.TaxAutoRules.Add(rule);
        }
        else
        {
            var existing = await _db.TaxAutoRules.FirstOrDefaultAsync(r => r.Id == rule.Id && r.UserId == userId)
                ?? throw new InvalidOperationException("Правило не найдено");
            existing.Name = rule.Name;
            existing.PaymentName = rule.PaymentName;
            existing.Formula = rule.Formula;
            existing.Period = rule.Period;
            existing.DueDayOfMonth = rule.DueDayOfMonth;
            existing.DueMonthOffset = rule.DueMonthOffset;
            existing.IsEnabled = rule.IsEnabled;
            existing.SortOrder = rule.SortOrder;
            rule = existing;
        }

        await _db.SaveChangesAsync();
        return rule;
    }

    public async Task DeleteAsync(int id, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var r = await _db.TaxAutoRules.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (r == null) return;
        _db.TaxAutoRules.Remove(r);
        await _db.SaveChangesAsync();
    }

    public async Task<List<TaxRulePreview>> PreviewAsync(string userId, DateTime? referenceDate = null)
    {
        var refDate = (referenceDate ?? DateTime.Today).Date;
        var rules = await ListAsync(userId);
        var previews = new List<TaxRulePreview>();

        foreach (var rule in rules.Where(r => r.IsEnabled))
        {
            var (start, end) = GetPeriodBounds(rule.Period, refDate);
            var totals = await _financeSummary.GetPeriodTotalsAsync(userId, start, end);
            var eval = TaxFormulaEvaluator.TryEvaluate(rule.Formula, new TaxFormulaContext { Income = totals.Income, Expenses = totals.Expenses });
            var due = GetDueDate(end, rule);
            var plannedName = TaxPlannedPaymentHelper.BuildName(rule, start);

            previews.Add(new TaxRulePreview
            {
                Rule = rule,
                Income = totals.Income,
                Expenses = totals.Expenses,
                OperationCount = totals.OperationCount,
                CalculatedAmount = eval.Ok ? eval.Value : 0,
                PeriodStart = start,
                PeriodEnd = end,
                DueDate = due,
                PlannedPaymentName = plannedName,
                Error = eval.Ok ? null : eval.Error
            });
        }

        return previews;
    }

    public async Task<TaxRuleGenerateResult> GeneratePaymentsAsync(string userId, bool skipExisting = true, DateTime? referenceDate = null)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var previews = await PreviewAsync(userId, referenceDate);
        var messages = new List<string>();
        var created = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var p in previews)
        {
            var one = await TryCreatePaymentCoreAsync(userId, p, skipExisting);
            if (one.Created) created++;
            else if (one.IsError) errors++;
            else skipped++;
            if (!string.IsNullOrEmpty(one.Message))
                messages.Add(one.Message);
        }

        return new TaxRuleGenerateResult
        {
            CreatedCount = created,
            SkippedCount = skipped,
            ErrorCount = errors,
            Messages = messages
        };
    }

    public async Task<TaxRuleGenerateResult> CreatePaymentFromPreviewAsync(string userId, TaxRulePreview preview)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var one = await TryCreatePaymentCoreAsync(userId, preview, skipExisting: true);
        return new TaxRuleGenerateResult
        {
            CreatedCount = one.Created ? 1 : 0,
            SkippedCount = one.Created || one.IsError ? 0 : 1,
            ErrorCount = one.IsError ? 1 : 0,
            Messages = string.IsNullOrEmpty(one.Message) ? new List<string>() : new List<string> { one.Message }
        };
    }

    private async Task<(bool Created, bool IsError, string Message)> TryCreatePaymentCoreAsync(
        string userId, TaxRulePreview p, bool skipExisting)
    {
        if (!string.IsNullOrEmpty(p.Error))
            return (false, true, $"{p.Rule.Name}: {p.Error}");

        if (p.CalculatedAmount <= 0)
            return (false, true, $"{p.Rule.Name}: сумма 0 — подставьте сводку за период или измените формулу");

        var paymentName = string.IsNullOrWhiteSpace(p.PlannedPaymentName)
            ? TaxPlannedPaymentHelper.BuildName(p.Rule, p.PeriodStart)
            : p.PlannedPaymentName;

        if (skipExisting && await PaymentAlreadyExistsAsync(userId, paymentName, p.DueDate))
            return (false, false, $"{paymentName}: уже в плане (срок {p.DueDate:dd.MM.yyyy})");

        await _taxService.AddTaxAsync(new TaxPayment
        {
            UserId = userId,
            Name = paymentName,
            Amount = p.CalculatedAmount,
            DueDate = p.DueDate
        });
        return (true, false, $"Создан: {paymentName} — {p.CalculatedAmount:N2}{BynCurrency.Suffix}, срок {p.DueDate:dd.MM.yyyy}");
    }

    private async Task<bool> PaymentAlreadyExistsAsync(string userId, string paymentName, DateTime dueDate)
    {
        var dueStart = dueDate.Date.AddDays(-3);
        var dueEnd = dueDate.Date.AddDays(4);
        return await _db.TaxPayments.AnyAsync(t =>
            t.UserId == userId && !t.IsPaid &&
            t.Name == paymentName &&
            t.DueDate >= dueStart && t.DueDate < dueEnd);
    }

    public async Task SyncRulesForTaxSystemAsync(string userId, TaxSystem taxSystem, bool replaceExisting = false)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var hasRules = await _db.TaxAutoRules.AnyAsync(r => r.UserId == userId);
        if (!hasRules)
        {
            await EnsureDefaultRulesAsync(userId, taxSystem);
            return;
        }

        if (!replaceExisting)
            return;

        var rules = await _db.TaxAutoRules.Where(r => r.UserId == userId).ToListAsync();
        _db.TaxAutoRules.RemoveRange(rules);
        await _db.SaveChangesAsync();
        await EnsureDefaultRulesAsync(userId, taxSystem);
    }

    public async Task EnsureDefaultRulesAsync(string userId, TaxSystem? taxSystem)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        if (await _db.TaxAutoRules.AnyAsync(r => r.UserId == userId))
            return;

        (string Name, string PayName, string Formula, TaxRulePeriod Period)[] defaults = taxSystem switch
        {
            TaxSystem.OSN =>
            [
                ("Подоходный / прибыль (оценка)", "Подоходный", "max(0, income - expenses) * 0.16", TaxRulePeriod.Quarterly),
                ("НДС (оценка, ЮЛ)", "НДС", "income * 20 / 120", TaxRulePeriod.Monthly),
                ("ФСЗН (оценка)", "ФСЗН", "income * 0.35", TaxRulePeriod.Monthly)
            ],
            TaxSystem.NPD =>
            [
                ("НПД (оценка 6% от выручки)", "НПД", "income * 0.06", TaxRulePeriod.Monthly)
            ],
            TaxSystem.UnifiedTax =>
            [
                ("Единый налог (сумма вручную)", "Единый", "0", TaxRulePeriod.Monthly)
            ],
            TaxSystem.USN =>
            [
                ("УСН 6% от выручки (РБ)", "УСН", "income * 0.06", TaxRulePeriod.Quarterly),
                ("ФСЗН (оценка)", "ФСЗН", "income * 0.35", TaxRulePeriod.Monthly)
            ],
            _ => Array.Empty<(string, string, string, TaxRulePeriod)>()
        };

        var order = 1;
        foreach (var (name, payName, formula, period) in defaults)
        {
            var enabled = formula != "0";
            _db.TaxAutoRules.Add(new TaxAutoRule
            {
                UserId = userId,
                Name = name,
                PaymentName = payName,
                Formula = formula,
                Period = period,
                DueDayOfMonth = 25,
                DueMonthOffset = 1,
                IsEnabled = enabled,
                SortOrder = order++
            });
        }

        await _db.SaveChangesAsync();
    }

    private static (DateTime Start, DateTime End) GetPeriodBounds(TaxRulePeriod period, DateTime refDate)
    {
        return period switch
        {
            TaxRulePeriod.Monthly => (
                new DateTime(refDate.Year, refDate.Month, 1).AddMonths(-1),
                new DateTime(refDate.Year, refDate.Month, 1).AddDays(-1)),
            TaxRulePeriod.Yearly => (
                new DateTime(refDate.Year - 1, 1, 1),
                new DateTime(refDate.Year - 1, 12, 31)),
            _ => GetQuarterBounds(refDate)
        };
    }

    private static (DateTime Start, DateTime End) GetQuarterBounds(DateTime refDate)
    {
        var q = (refDate.Month - 1) / 3;
        var year = refDate.Year;
        if (q == 0)
        {
            year--;
            q = 3;
        }
        else
        {
            q--;
        }

        var startMonth = q * 3 + 1;
        var endMonth = startMonth + 2;
        var start = new DateTime(year, startMonth, 1);
        var end = new DateTime(year, endMonth, DateTime.DaysInMonth(year, endMonth));
        return (start, end);
    }

    private static DateTime GetDueDate(DateTime periodEnd, TaxAutoRule rule)
    {
        var dueMonth = periodEnd.AddMonths(rule.DueMonthOffset);
        var day = Math.Clamp(rule.DueDayOfMonth, 1, 28);
        var daysInMonth = DateTime.DaysInMonth(dueMonth.Year, dueMonth.Month);
        return new DateTime(dueMonth.Year, dueMonth.Month, Math.Min(day, daysInMonth));
    }

}

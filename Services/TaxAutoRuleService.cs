using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class TaxAutoRuleService : ITaxAutoRuleService
{
    private readonly ApplicationDbContext _db;
    private readonly ITaxService _taxService;
    private readonly IDataScopeService _dataScope;

    public TaxAutoRuleService(ApplicationDbContext db, ITaxService taxService, IDataScopeService dataScope)
    {
        _db = db;
        _taxService = taxService;
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
            var (income, expenses) = await SumPeriodAsync(userId, start, end);
            var eval = TaxFormulaEvaluator.TryEvaluate(rule.Formula, new TaxFormulaContext { Income = income, Expenses = expenses });
            var due = GetDueDate(end, rule);

            previews.Add(new TaxRulePreview
            {
                Rule = rule,
                Income = income,
                Expenses = expenses,
                CalculatedAmount = eval.Ok ? eval.Value : 0,
                PeriodStart = start,
                PeriodEnd = end,
                DueDate = due,
                Error = eval.Ok ? null : eval.Error
            });
        }

        return previews;
    }

    public async Task<TaxRuleGenerateResult> GeneratePaymentsAsync(string userId, bool skipExisting = true, DateTime? referenceDate = null)
    {
        var previews = await PreviewAsync(userId, referenceDate);
        var messages = new List<string>();
        var created = 0;
        var skipped = 0;

        foreach (var p in previews)
        {
            if (!string.IsNullOrEmpty(p.Error))
            {
                messages.Add($"{p.Rule.Name}: {p.Error}");
                skipped++;
                continue;
            }

            if (p.CalculatedAmount <= 0)
            {
                messages.Add($"{p.Rule.Name}: сумма 0 — платёж не создан");
                skipped++;
                continue;
            }

            var paymentName = BuildPaymentName(p.Rule, p.PeriodStart, p.PeriodEnd);

            if (skipExisting)
            {
                var exists = await _db.TaxPayments.AnyAsync(t =>
                    t.UserId == userId && !t.IsPaid &&
                    t.Name == paymentName &&
                    t.DueDate.Date == p.DueDate.Date &&
                    Math.Abs(t.Amount - p.CalculatedAmount) < 0.01m);
                if (exists)
                {
                    skipped++;
                    continue;
                }
            }

            await _taxService.AddTaxAsync(new TaxPayment
            {
                UserId = userId,
                Name = paymentName,
                Amount = p.CalculatedAmount,
                DueDate = p.DueDate
            });
            created++;
            messages.Add($"Создан: {paymentName} — {p.CalculatedAmount:N2} Br до {p.DueDate:dd.MM.yyyy}");
        }

        return new TaxRuleGenerateResult { CreatedCount = created, SkippedCount = skipped, Messages = messages };
    }

    public async Task EnsureDefaultRulesAsync(string userId, TaxSystem? taxSystem)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        if (await _db.TaxAutoRules.AnyAsync(r => r.UserId == userId))
            return;

        var defaults = taxSystem == TaxSystem.OSN
            ? new[]
            {
                ("НДС (оценка)", "НДС", "income * 20 / 120", TaxRulePeriod.Monthly),
                ("Налог на прибыль (оценка)", "Подоходный", "(income - income * 20 / 120) * 0.20", TaxRulePeriod.Quarterly),
                ("ФСЗН (35% от ФОТ)", "ФСЗН", "income * 0.35 * 0.34", TaxRulePeriod.Monthly)
            }
            : new[]
            {
                ("УСН 6% (доход)", "УСН", "income * 0.06", TaxRulePeriod.Quarterly),
                ("УСН 15% (доход−расход)", "УСН", "max(0, income - expenses) * 0.15", TaxRulePeriod.Quarterly),
                ("ФСЗН (оценка)", "ФСЗН", "income * 0.35", TaxRulePeriod.Monthly)
            };

        var order = 1;
        foreach (var (name, payName, formula, period) in defaults)
        {
            _db.TaxAutoRules.Add(new TaxAutoRule
            {
                UserId = userId,
                Name = name,
                PaymentName = payName,
                Formula = formula,
                Period = period,
                DueDayOfMonth = 25,
                DueMonthOffset = 1,
                IsEnabled = true,
                SortOrder = order++
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task<(decimal Income, decimal Expenses)> SumPeriodAsync(string userId, DateTime start, DateTime end)
    {
        var txs = await _db.Transactions
            .Where(t => t.UserId == userId && t.IsConfirmed && t.Date >= start && t.Date <= end)
            .Select(t => t.Amount)
            .ToListAsync();

        var income = txs.Where(a => a > 0).Sum();
        var expenses = Math.Abs(txs.Where(a => a < 0).Sum());
        return (income, expenses);
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

    private static string BuildPaymentName(TaxAutoRule rule, DateTime start, DateTime end) =>
        rule.Period switch
        {
            TaxRulePeriod.Monthly => $"{rule.PaymentName} {start:MMM yyyy}",
            TaxRulePeriod.Yearly => $"{rule.PaymentName} {start:yyyy}",
            _ => $"{rule.PaymentName} Q{(start.Month - 1) / 3 + 1} {start:yyyy}"
        };
}

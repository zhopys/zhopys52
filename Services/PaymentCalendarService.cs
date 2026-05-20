using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class PaymentCalendarService : IPaymentCalendarService
{
    private readonly ApplicationDbContext _db;
    private readonly ITaxService _taxService;
    private readonly ITransactionService _transactionService;
    private readonly IDataScopeService _dataScope;

    public PaymentCalendarService(ApplicationDbContext db, ITaxService taxService, ITransactionService transactionService, IDataScopeService dataScope)
    {
        _db = db;
        _taxService = taxService;
        _transactionService = transactionService;
        _dataScope = dataScope;
    }

    public async Task<PaymentCalendarDto> BuildMonthAsync(string userId, int year, int month)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var today = DateTime.Today;

        var reminders = await _db.Reminders
            .Where(r => r.UserId == userId && !r.IsArchived)
            .ToListAsync();

        var taxes = await _db.TaxPayments
            .Where(t => t.UserId == userId && t.DueDate >= monthStart && t.DueDate <= monthEnd)
            .ToListAsync();

        var debts = await _db.Debts
            .Where(d => d.UserId == userId && d.DueDate.HasValue &&
                        d.DueDate >= monthStart && d.DueDate <= monthEnd)
            .ToListAsync();

        var items = new List<PaymentCalendarItemDto>();

        foreach (var r in reminders.Where(r => !r.IsPaid || r.Frequency != ReminderFrequency.OneTime))
        {
            if (r.SnoozedUntil.HasValue && r.SnoozedUntil.Value.Date > today)
                continue;

            if (r.Frequency == ReminderFrequency.OneTime)
            {
                if (r.Date >= monthStart && r.Date <= monthEnd)
                    items.Add(MapReminder(r, today, r.Date));
                continue;
            }

            foreach (var occurrence in RecurringCalendarHelper.OccurrencesInRange(r.Date, r.Frequency, monthStart, monthEnd))
            {
                if (r.IsPaid && occurrence < r.Date)
                    continue;
                items.Add(MapReminder(r, today, occurrence));
            }
        }

        foreach (var t in taxes)
        {
            items.Add(MapTax(t, today));
        }

        foreach (var d in debts)
        {
            items.Add(MapDebt(d, today));
        }

        items = items.OrderBy(i => i.Date).ThenBy(i => i.Title).ToList();

        var outflows = items.Where(i => !i.IsInflow).ToList();
        return new PaymentCalendarDto
        {
            Year = year,
            Month = month,
            Items = items,
            TotalInflow = items.Where(i => i.IsInflow).Sum(i => i.Amount),
            TotalOutflow = outflows.Sum(i => i.Amount),
            UnpaidOutflow = outflows.Where(i => !i.IsPaid).Sum(i => i.Amount),
            OverdueCount = outflows.Count(i => i.Status == PaymentCalendarStatus.Overdue),
            Counterparties = items
                .Select(i => i.Counterparty)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList()
        };
    }

    public async Task<PaymentCalendarItemDto> AddPaymentAsync(PaymentCreateRequest request, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        if (request.Amount <= 0)
            throw new ArgumentException("Сумма должна быть больше нуля");

        var purpose = string.IsNullOrWhiteSpace(request.Purpose)
            ? request.Counterparty
            : request.Purpose.Trim();

        var reminder = new Reminder
        {
            Name = purpose,
            Amount = request.Amount,
            Category = request.Category?.Trim() ?? "",
            Frequency = request.Frequency,
            Date = request.Date.Date,
            UserId = userId,
            IsPaid = false,
            Notes = string.IsNullOrWhiteSpace(request.Counterparty)
                ? null
                : $"Контрагент: {request.Counterparty.Trim()}"
        };

        _db.Reminders.Add(reminder);
        await _db.SaveChangesAsync();
        return MapReminder(reminder, DateTime.Today, reminder.Date);
    }

    public Task MarkPaidAsync(string itemKey, string userId) =>
        MarkPaidBulkAsync(new[] { itemKey }, userId);

    public async Task<PaymentBulkPayResult> MarkPaidBulkAsync(IEnumerable<string> itemKeys, string userId)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var keys = itemKeys.Distinct().ToList();
        var success = 0;
        var skipped = 0;

        foreach (var key in keys)
        {
            if (!TryParseKey(key, out var kind, out var id))
            {
                skipped++;
                continue;
            }

            try
            {
                switch (kind)
                {
                    case PaymentCalendarSourceKind.Reminder:
                        if (await MarkReminderPaidAsync(id, userId)) success++;
                        else skipped++;
                        break;
                    case PaymentCalendarSourceKind.Tax:
                        await _taxService.MarkAsPaidAsync(id, userId);
                        success++;
                        break;
                    case PaymentCalendarSourceKind.Debt:
                        if (await MarkDebtPaidAsync(id, userId)) success++;
                        else skipped++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }
            catch
            {
                skipped++;
            }
        }

        var bankNote = keys.Count > 1
            ? " Массовая оплата через банк будет доступна после подключения интеграции."
            : "";

        return new PaymentBulkPayResult
        {
            SuccessCount = success,
            SkippedCount = skipped,
            Message = success > 0
                ? $"Отмечено оплаченным: {success}.{bankNote}"
                : "Не удалось отметить выбранные платежи."
        };
    }

    private async Task<bool> MarkReminderPaidAsync(int id, string userId)
    {
        var reminder = await _db.Reminders.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
        if (reminder == null || reminder.IsPaid) return false;

        await _transactionService.CreateAsync(new Transaction
        {
            Date = DateTime.Today,
            Amount = -Math.Abs(reminder.Amount),
            Description = reminder.Name,
            Category = reminder.Category ?? "",
            ProjectId = reminder.ProjectId
        }, userId);

        if (reminder.Frequency == ReminderFrequency.OneTime)
        {
            reminder.IsPaid = true;
            reminder.PaidDate = DateTime.Now;
        }
        else
        {
            reminder.Date = AdvanceDate(reminder.Date, reminder.Frequency);
            reminder.NotificationSentDate = null;
        }

        await _db.SaveChangesAsync();
        return true;
    }

    private async Task<bool> MarkDebtPaidAsync(int id, string userId)
    {
        var debt = await _db.Debts.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);
        if (debt == null || debt.IsSettled) return false;

        var remaining = debt.Amount - debt.PaidAmount;
        if (remaining <= 0) return false;

        debt.PaidAmount = debt.Amount;
        debt.IsSettled = true;

        var isInflow = debt.Type == DebtType.Receivable;
        await _transactionService.CreateAsync(new Transaction
        {
            Date = DateTime.Today,
            Amount = isInflow ? remaining : -remaining,
            Description = $"{(isInflow ? "Поступление" : "Оплата")}: {debt.CounterpartyName}",
            Category = isInflow ? "Доход" : "Прочее",
            CounterpartyId = debt.CounterpartyId,
            Counterparty = debt.CounterpartyName
        }, userId);

        await _db.SaveChangesAsync();
        return true;
    }

    private static DateTime AdvanceDate(DateTime date, ReminderFrequency frequency) =>
        RecurringCalendarHelper.StepForward(date, frequency);

    private static PaymentCalendarItemDto MapReminder(Reminder r, DateTime today, DateTime occurrenceDate)
    {
        var counterparty = ExtractCounterparty(r.Notes);
        var paymentType = ResolvePaymentTypeFromReminder(r);
        var isPaid = r.IsPaid && r.Frequency == ReminderFrequency.OneTime;
        var status = ResolveStatus(isPaid, occurrenceDate, today);

        return new PaymentCalendarItemDto
        {
            Key = $"reminder-{r.Id}-{occurrenceDate:yyyyMMdd}",
            Date = occurrenceDate.Date,
            Title = r.Name,
            Purpose = r.Name,
            Counterparty = counterparty,
            Category = r.Category ?? "",
            Amount = r.Amount,
            IsInflow = false,
            Source = "напоминание",
            SourceKind = PaymentCalendarSourceKind.Reminder,
            PaymentType = paymentType,
            Status = status,
            IsPaid = isPaid,
            CanMarkPaid = !isPaid,
            ReminderId = r.Id
        };
    }

    private static PaymentCalendarPaymentType ResolvePaymentTypeFromReminder(Reminder r) => r.ReminderType switch
    {
        ReminderType.Tax => PaymentCalendarPaymentType.Tax,
        ReminderType.Rent => PaymentCalendarPaymentType.Rent,
        ReminderType.Salary => PaymentCalendarPaymentType.Salary,
        ReminderType.Bill or ReminderType.Subscription => ResolvePaymentType("счёт", r.Category, r.Name),
        _ => ResolvePaymentType("напоминание", r.Category, r.Name)
    };

    private static PaymentCalendarItemDto MapTax(TaxPayment t, DateTime today)
    {
        var status = ResolveStatus(t.IsPaid, t.DueDate, today);
        return new PaymentCalendarItemDto
        {
            Key = $"tax-{t.Id}",
            Date = t.DueDate.Date,
            Title = t.Name,
            Purpose = $"Налог: {t.Name}",
            Counterparty = "Бюджет / ФНС",
            Category = "Налоги",
            Amount = t.Amount,
            IsInflow = false,
            Source = "налог",
            SourceKind = PaymentCalendarSourceKind.Tax,
            PaymentType = PaymentCalendarPaymentType.Tax,
            Status = status,
            IsPaid = t.IsPaid,
            CanMarkPaid = !t.IsPaid,
            TaxId = t.Id
        };
    }

    private static PaymentCalendarItemDto MapDebt(Debt d, DateTime today)
    {
        var remaining = d.Amount - d.PaidAmount;
        var isInflow = d.Type == DebtType.Receivable;
        var isPaid = d.IsSettled || remaining <= 0;
        var status = ResolveStatus(isPaid, d.DueDate!.Value, today);
        var paymentType = ResolvePaymentType("долг", "", d.CounterpartyName);

        return new PaymentCalendarItemDto
        {
            Key = $"debt-{d.Id}",
            Date = d.DueDate!.Value.Date,
            Title = d.CounterpartyName,
            Purpose = d.Description ?? (isInflow ? "Поступление от контрагента" : "Оплата контрагенту"),
            Counterparty = d.CounterpartyName,
            Category = isInflow ? "Доход" : "Прочее",
            Amount = remaining > 0 ? remaining : d.Amount,
            IsInflow = isInflow,
            Source = isInflow ? "дебиторка" : "кредиторка",
            SourceKind = PaymentCalendarSourceKind.Debt,
            PaymentType = paymentType,
            Status = status,
            IsPaid = isPaid,
            CanMarkPaid = !isPaid,
            DebtId = d.Id
        };
    }

    private static PaymentCalendarStatus ResolveStatus(bool isPaid, DateTime date, DateTime today)
    {
        if (isPaid) return PaymentCalendarStatus.Paid;
        var days = (date.Date - today).Days;
        if (days < 0) return PaymentCalendarStatus.Overdue;
        if (days == 0) return PaymentCalendarStatus.DueToday;
        if (days == 1) return PaymentCalendarStatus.DueTomorrow;
        return PaymentCalendarStatus.Planned;
    }

    private static PaymentCalendarPaymentType ResolvePaymentType(string source, string category, string title)
    {
        if (source == "налог" || CategoryBucketHelper.IsTax(category) || CategoryBucketHelper.IsTax(title))
            return PaymentCalendarPaymentType.Tax;
        if (CategoryBucketHelper.IsRent(category) || CategoryBucketHelper.IsRent(title))
            return PaymentCalendarPaymentType.Rent;
        if (CategoryBucketHelper.IsPayroll(category) || CategoryBucketHelper.IsPayroll(title))
            return PaymentCalendarPaymentType.Salary;

        var combined = $"{category} {title}".ToLowerInvariant();
        if (combined.Contains("коммун") || combined.Contains("электр") || combined.Contains("вода") ||
            combined.Contains("связь") || combined.Contains("интернет") || combined.Contains("газ"))
            return PaymentCalendarPaymentType.Utilities;

        return PaymentCalendarPaymentType.Other;
    }

    private static string ExtractCounterparty(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";
        const string prefix = "Контрагент:";
        if (notes.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return notes[prefix.Length..].Trim();
        return "";
    }

    private static bool TryParseKey(string key, out PaymentCalendarSourceKind kind, out int id)
    {
        kind = PaymentCalendarSourceKind.Reminder;
        id = 0;
        var parts = key.Split('-', 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], out id)) return false;
        kind = parts[0] switch
        {
            "reminder" => PaymentCalendarSourceKind.Reminder,
            "tax" => PaymentCalendarSourceKind.Tax,
            "debt" => PaymentCalendarSourceKind.Debt,
            _ => PaymentCalendarSourceKind.Forecast
        };
        return kind != PaymentCalendarSourceKind.Forecast;
    }
}

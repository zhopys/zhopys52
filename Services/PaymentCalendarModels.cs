using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public enum PaymentCalendarSourceKind
{
    Reminder,
    Tax,
    Debt,
    Forecast
}

public enum PaymentCalendarPaymentType
{
    All,
    Tax,
    Rent,
    Salary,
    Utilities,
    Other
}

public enum PaymentCalendarStatus
{
    Paid,
    Planned,
    DueToday,
    DueTomorrow,
    Overdue
}

public sealed class PaymentCalendarDto
{
    public int Year { get; init; }
    public int Month { get; init; }
    public IReadOnlyList<PaymentCalendarItemDto> Items { get; init; } = Array.Empty<PaymentCalendarItemDto>();
    public decimal TotalInflow { get; init; }
    public decimal TotalOutflow { get; init; }
    public decimal UnpaidOutflow { get; init; }
    public int OverdueCount { get; init; }
    public IReadOnlyList<string> Counterparties { get; init; } = Array.Empty<string>();
}

public sealed class PaymentCalendarItemDto
{
    public string Key { get; init; } = "";
    public DateTime Date { get; init; }
    public string Title { get; init; } = "";
    public string Purpose { get; init; } = "";
    public string Counterparty { get; init; } = "";
    public string Category { get; init; } = "";
    public decimal Amount { get; init; }
    public bool IsInflow { get; init; }
    public string Source { get; init; } = "";
    public PaymentCalendarSourceKind SourceKind { get; init; }
    public PaymentCalendarPaymentType PaymentType { get; init; }
    public PaymentCalendarStatus Status { get; init; }
    public bool IsPaid { get; init; }
    public bool CanMarkPaid { get; init; }
    public int? ReminderId { get; init; }
    public int? TaxId { get; init; }
    public int? DebtId { get; init; }
}

public sealed class PaymentCreateRequest
{
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = "";
    public string Counterparty { get; set; } = "";
    public string Purpose { get; set; } = "";
    public ReminderFrequency Frequency { get; set; } = ReminderFrequency.OneTime;
}

public sealed class PaymentBulkPayResult
{
    public int SuccessCount { get; init; }
    public int SkippedCount { get; init; }
    public string Message { get; init; } = "";
}

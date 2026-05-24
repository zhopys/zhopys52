using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public static class ReminderScheduleHelper
{
    /// <summary>Отложить платёж на N дней (сдвигает дату, не скрывает из списков).</summary>
    public static void PostponeByDays(Reminder reminder, int days)
    {
        if (days <= 0) return;
        var baseDate = reminder.Date.Date < DateTime.Today ? DateTime.Today : reminder.Date.Date;
        reminder.Date = baseDate.AddDays(days);
        reminder.SnoozedUntil = null;
        reminder.NotificationSentDate = null;
    }

    public static bool IsSnoozedActive(Reminder reminder, DateTime? today = null)
    {
        today ??= DateTime.Today;
        return reminder.SnoozedUntil.HasValue && reminder.SnoozedUntil.Value.Date > today.Value.Date;
    }

    public static bool MatchesUpcoming(Reminder reminder, DateTime today) =>
        !reminder.IsArchived && !reminder.IsPaid && reminder.Date.Date >= today && !IsSnoozedActive(reminder, today);

    public static bool MatchesOverdue(Reminder reminder, DateTime today) =>
        !reminder.IsArchived && !reminder.IsPaid && reminder.Date.Date < today && !IsSnoozedActive(reminder, today);

    public static bool ShouldNotifyToday(Reminder reminder, DateTime today)
    {
        if (reminder.IsPaid || reminder.IsArchived || IsSnoozedActive(reminder, today))
            return false;
        if (reminder.Date.Date < today)
            return false;

        var daysUntil = (reminder.Date.Date - today).Days;
        var lead = reminder.NotifyDaysBefore < 0 ? 0 : reminder.NotifyDaysBefore;
        return daysUntil <= lead;
    }
}

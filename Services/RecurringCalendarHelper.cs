using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public static class RecurringCalendarHelper
{
    public static IEnumerable<DateTime> OccurrencesInRange(
        DateTime anchor,
        ReminderFrequency frequency,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        var start = rangeStart.Date;
        var end = rangeEnd.Date;
        var d = anchor.Date;

        if (frequency == ReminderFrequency.OneTime)
        {
            if (d >= start && d <= end)
                yield return d;
            yield break;
        }

        while (d > start)
            d = StepBack(d, frequency);

        while (d < start)
            d = StepForward(d, frequency);

        while (d <= end)
        {
            yield return d;
            d = StepForward(d, frequency);
        }
    }

    public static DateTime StepForward(DateTime date, ReminderFrequency frequency) => frequency switch
    {
        ReminderFrequency.Daily => date.AddDays(1),
        ReminderFrequency.Weekly => date.AddDays(7),
        ReminderFrequency.Monthly => date.AddMonths(1),
        ReminderFrequency.Quarterly => date.AddMonths(3),
        ReminderFrequency.Yearly => date.AddYears(1),
        _ => date
    };

    private static DateTime StepBack(DateTime date, ReminderFrequency frequency) => frequency switch
    {
        ReminderFrequency.Daily => date.AddDays(-1),
        ReminderFrequency.Weekly => date.AddDays(-7),
        ReminderFrequency.Monthly => date.AddMonths(-1),
        ReminderFrequency.Quarterly => date.AddMonths(-3),
        ReminderFrequency.Yearly => date.AddYears(-1),
        _ => date
    };
}

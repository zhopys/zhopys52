using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public static class TaxDueDateHelper
{
    /// <summary>Срок уплаты после окончания налогового периода (типично 25-е число следующего месяца, РБ).</summary>
    public static DateTime DueAfterPeriodEnd(DateTime periodEnd, int monthOffset = 1, int dayOfMonth = 25)
    {
        var dueMonth = periodEnd.AddMonths(monthOffset);
        var day = Math.Clamp(dayOfMonth, 1, 28);
        var daysInMonth = DateTime.DaysInMonth(dueMonth.Year, dueMonth.Month);
        return new DateTime(dueMonth.Year, dueMonth.Month, Math.Min(day, daysInMonth));
    }

    public static DateTime GetCalculatorDueDate(TaxSystem? system, string calcPeriod, DateTime today)
    {
        var (start, end) = GetPeriodBounds(calcPeriod, today);
        return system switch
        {
            TaxSystem.USN => DueAfterPeriodEnd(end, monthOffset: 1, dayOfMonth: 25),
            TaxSystem.OSN when calcPeriod == "quarter" => DueAfterPeriodEnd(end, 1, 25),
            TaxSystem.NPD or TaxSystem.UnifiedTax => DueAfterPeriodEnd(end, 1, 25),
            _ => DueAfterPeriodEnd(end, 1, 25)
        };
    }

    public static (DateTime Start, DateTime End) GetPeriodBounds(string calcPeriod, DateTime today)
    {
        return calcPeriod switch
        {
            "month" => (
                new DateTime(today.Year, today.Month, 1).AddMonths(-1),
                new DateTime(today.Year, today.Month, 1).AddDays(-1)),
            "year" => (
                new DateTime(today.Year - 1, 1, 1),
                new DateTime(today.Year - 1, 12, 31)),
            _ => GetLastQuarterBounds(today)
        };
    }

    private static (DateTime Start, DateTime End) GetLastQuarterBounds(DateTime refDate)
    {
        var q = (refDate.Month - 1) / 3;
        var year = refDate.Year;
        if (q == 0) { year--; q = 3; } else { q--; }
        var startMonth = q * 3 + 1;
        var endMonth = startMonth + 2;
        return (
            new DateTime(year, startMonth, 1),
            new DateTime(year, endMonth, DateTime.DaysInMonth(year, endMonth)));
    }
}

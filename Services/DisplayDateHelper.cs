using System.Globalization;

namespace MiniFinance.Services;

public static class DisplayDateHelper
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Short(DateTime date) => date.ToString("dd.MM.yyyy", Ru);

    public static string MonthDay(DateTime date) => date.ToString("dd.MM", Ru);

    public static string Range(DateTime start, DateTime end) => $"{Short(start)} – {Short(end)}";

    public static string MonthYear(DateTime date) => date.ToString("MMMM yyyy", Ru);
}

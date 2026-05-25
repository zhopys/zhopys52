using System.Globalization;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

/// <summary>Имена и проверка дубликатов плановых налоговых платежей.</summary>
public static class TaxPlannedPaymentHelper
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string BuildName(TaxAutoRule rule, DateTime periodStart) =>
        $"{rule.Name.Trim()} ({FormatPeriodLabel(rule.Period, periodStart)})";

    public static string FormatPeriodLabel(TaxRulePeriod period, DateTime periodStart) => period switch
    {
        TaxRulePeriod.Monthly => periodStart.ToString("MMMM yyyy", Ru),
        TaxRulePeriod.Yearly => periodStart.ToString("yyyy", Ru),
        _ => $"Q{(periodStart.Month - 1) / 3 + 1} {periodStart:yyyy}"
    };

    public static string FormatCalcPeriodLabel(string calcPeriod, DateTime periodStart) => calcPeriod switch
    {
        "month" => periodStart.ToString("MMMM yyyy", Ru),
        "year" => periodStart.ToString("yyyy", Ru),
        _ => $"Q{(periodStart.Month - 1) / 3 + 1} {periodStart:yyyy}"
    };

    public static string BuildCalculatorPaymentName(string lineName, string? suggestedPaymentName, string calcPeriod, DateTime periodStart)
    {
        var (typeSelect, custom) = TaxFormHelper.MapToFormFields(lineName, suggestedPaymentName);
        var baseName = TaxFormHelper.ResolvePaymentName(typeSelect, custom);
        return $"{baseName} ({FormatCalcPeriodLabel(calcPeriod, periodStart)})";
    }

    /// <summary>Проверка: уже есть неоплаченный плановый платёж с тем же именем и сроком (±3 дня).</summary>
    public static bool MatchesExisting(TaxPayment existing, string paymentName, DateTime dueDate)
    {
        if (existing.IsPaid || existing.Name != paymentName)
            return false;
        var dueStart = dueDate.Date.AddDays(-3);
        var dueEnd = dueDate.Date.AddDays(4);
        return existing.DueDate >= dueStart && existing.DueDate < dueEnd;
    }
}

using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public sealed class TransactionTaxSuggestion
{
    public string PaymentType { get; init; } = "";
    public decimal Amount { get; init; }
    public DateTime DueDate { get; init; }
    public string Hint { get; init; } = "";
}

public static class TransactionTaxLinkHelper
{
    private const decimal FsznPayrollRate = 0.34m;

    public static IReadOnlyList<TransactionTaxSuggestion> GetSuggestions(
        TransactionTaxLine line,
        TaxSystem system,
        TaxpayerKind taxpayerKind)
    {
        if (line.Treatment is TransactionTaxTreatment.TaxPayment)
            return Array.Empty<TransactionTaxSuggestion>();

        var list = new List<TransactionTaxSuggestion>();

        if (line.AccruedTax > 0)
        {
            var type = ResolveIncomePaymentType(line, system, taxpayerKind);
            list.Add(new TransactionTaxSuggestion
            {
                PaymentType = type,
                Amount = line.AccruedTax,
                DueDate = SuggestDueDate(line.Date, type, system),
                Hint = line.Note
            });
        }

        if (IsPayroll(line) && system is TaxSystem.USN or TaxSystem.OSN)
        {
            var salary = Math.Abs(line.Amount);
            if (salary > 0)
            {
                list.Add(new TransactionTaxSuggestion
                {
                    PaymentType = "ФСЗН",
                    Amount = Math.Round(salary * FsznPayrollRate, 2),
                    DueDate = SuggestDueDate(line.Date, "ФСЗН", system),
                    Hint = "Оценка взносов с фонда оплаты труда (~34%)"
                });
            }
        }

        return list;
    }

    public static string BuildPaymentName(TransactionTaxSuggestion suggestion, TransactionTaxLine line)
    {
        var label = Shorten(line.Description, 28);
        return $"{suggestion.PaymentType} · {label} ({DisplayDateHelper.Short(line.Date)})";
    }

    private static bool IsPayroll(TransactionTaxLine line) =>
        CategoryBucketHelper.IsPayroll(line.Category)
        || CategoryBucketHelper.IsPayroll(line.Description);

    private static string ResolveIncomePaymentType(
        TransactionTaxLine line,
        TaxSystem system,
        TaxpayerKind taxpayerKind)
    {
        return system switch
        {
            TaxSystem.USN => "УСН",
            TaxSystem.NPD => "НПД",
            TaxSystem.UnifiedTax => "Единый налог",
            TaxSystem.OSN when line.RateLabel.Contains("НДС", StringComparison.OrdinalIgnoreCase) => "НДС",
            TaxSystem.OSN => taxpayerKind == TaxpayerKind.IndividualEntrepreneur ? "Подоходный" : "Подоходный",
            _ => "УСН"
        };
    }

    public static DateTime SuggestDueDate(DateTime transactionDate, string paymentType, TaxSystem system)
    {
        var txDate = transactionDate.Date;

        if (paymentType == "ФСЗН")
        {
            var monthEnd = EndOfMonth(txDate);
            return TaxDueDateHelper.DueAfterPeriodEnd(monthEnd, monthOffset: 1, dayOfMonth: 20);
        }

        if (system == TaxSystem.USN)
        {
            var quarterEnd = EndOfQuarter(txDate);
            return TaxDueDateHelper.DueAfterPeriodEnd(quarterEnd, monthOffset: 1, dayOfMonth: 25);
        }

        var periodEnd = EndOfMonth(txDate);
        return TaxDueDateHelper.DueAfterPeriodEnd(periodEnd, monthOffset: 1, dayOfMonth: 25);
    }

    private static DateTime EndOfMonth(DateTime date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    private static DateTime EndOfQuarter(DateTime date)
    {
        var quarter = (date.Month - 1) / 3;
        var endMonth = (quarter + 1) * 3;
        return new DateTime(date.Year, endMonth, DateTime.DaysInMonth(date.Year, endMonth));
    }

    private static string Shorten(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "операция";
        text = text.Trim();
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}

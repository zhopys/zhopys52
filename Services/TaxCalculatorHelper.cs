using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public sealed class TaxCalculationResult
{
    public decimal TaxAmount { get; init; }
    public string Description { get; init; } = "";
    public decimal EffectiveRate { get; init; }
}

public static class TaxCalculatorHelper
{
    public static TaxCalculationResult Calculate(TaxSystem system, decimal income, decimal expenses = 0)
    {
        if (income <= 0)
            return new TaxCalculationResult { Description = "Укажите доход за период" };

        return system switch
        {
            TaxSystem.USN => CalculateUsn(income, expenses),
            TaxSystem.OSN => CalculateOsn(income),
            _ => CalculateUsn(income, expenses)
        };
    }

    private static TaxCalculationResult CalculateUsn(decimal income, decimal expenses)
    {
        var rate6 = income * 0.06m;
        var profit = Math.Max(0, income - expenses);
        var rate15 = profit * 0.15m;
        var use15 = expenses > 0 && rate15 < rate6;
        var amount = use15 ? rate15 : rate6;
        var rate = use15 ? 15m : 6m;
        var baseLabel = use15 ? "доход минус расход (15%)" : "доход (6%)";
        return new TaxCalculationResult
        {
            TaxAmount = Math.Round(amount, 2),
            EffectiveRate = rate,
            Description = $"УСН {baseLabel}: {income:N0} Br → налог {amount:N2} Br"
        };
    }

    private static TaxCalculationResult CalculateOsn(decimal income)
    {
        var vat = income * 20m / 120m;
        var profitTax = (income - vat) * 0.20m;
        var total = vat + profitTax;
        return new TaxCalculationResult
        {
            TaxAmount = Math.Round(total, 2),
            EffectiveRate = income > 0 ? Math.Round(total / income * 100, 1) : 0,
            Description = $"ОСН (оценка): НДС ~{vat:N0} Br + налог на прибыль ~{profitTax:N0} Br"
        };
    }

    public static string GetPaymentStatusLabel(TaxPayment tax)
    {
        if (tax.IsPaid) return "Оплачено";
        if (tax.PaidAmount > 0 && tax.PaidAmount < tax.Amount) return "Частично оплачено";
        if (tax.DueDate.Date < DateTime.Today) return "Просрочено";
        return "Не оплачено";
    }
}

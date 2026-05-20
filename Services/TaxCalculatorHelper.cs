using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public sealed class TaxCalcLine
{
    public string Name { get; init; } = "";
    public decimal Amount { get; init; }
}

public sealed class TaxCalculationResult
{
    public decimal TaxAmount { get; init; }
    public string Description { get; init; } = "";
    public decimal EffectiveRate { get; init; }
    public string SuggestedPaymentName { get; init; } = "УСН";
    public IReadOnlyList<TaxCalcLine> Lines { get; init; } = Array.Empty<TaxCalcLine>();
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
            SuggestedPaymentName = "УСН",
            Description = $"УСН {baseLabel}: {income:N0} Br → налог {amount:N2} Br",
            Lines = [new TaxCalcLine { Name = "УСН", Amount = Math.Round(amount, 2) }]
        };
    }

    private static TaxCalculationResult CalculateOsn(decimal income)
    {
        var vat = Math.Round(income * 20m / 120m, 2);
        var profitTax = Math.Round((income - vat) * 0.20m, 2);
        var total = vat + profitTax;
        return new TaxCalculationResult
        {
            TaxAmount = total,
            EffectiveRate = income > 0 ? Math.Round(total / income * 100, 1) : 0,
            SuggestedPaymentName = "НДС",
            Description = $"ОСН (оценка): НДС ~{vat:N0} Br + налог на прибыль ~{profitTax:N0} Br",
            Lines =
            [
                new TaxCalcLine { Name = "НДС", Amount = vat },
                new TaxCalcLine { Name = "Подоходный", Amount = profitTax }
            ]
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

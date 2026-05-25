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

public sealed class TaxCalculatorInput
{
    public TaxSystem System { get; init; } = TaxSystem.USN;
    public TaxpayerKind TaxpayerKind { get; init; } = TaxpayerKind.LegalEntity;
    public decimal Income { get; init; }
    public decimal Expenses { get; init; }
    public decimal IncomeFromIndividuals { get; init; }
    public decimal IncomeFromLegalEntities { get; init; }
    public decimal UnifiedTaxAmount { get; init; }
}

public static class TaxCalculatorHelper
{
    public static TaxCalculationResult Calculate(TaxCalculatorInput input)
    {
        if (input.System == TaxSystem.UnifiedTax)
            return CalculateUnified(input.UnifiedTaxAmount);

        if (input.System == TaxSystem.NPD)
            return CalculateNpd(input);

        if (input.Income <= 0 && input.IncomeFromIndividuals <= 0 && input.IncomeFromLegalEntities <= 0)
            return new TaxCalculationResult { Description = "Укажите доход (выручку) за период" };

        return input.System switch
        {
            TaxSystem.USN => CalculateUsnRb(input.Income),
            TaxSystem.OSN => CalculateOsnRb(input),
            _ => CalculateUsnRb(input.Income)
        };
    }

    public static TaxCalculationResult Calculate(TaxSystem system, decimal income, decimal expenses = 0) =>
        Calculate(new TaxCalculatorInput { System = system, Income = income, Expenses = expenses });

    private static TaxCalculationResult CalculateUsnRb(decimal income)
    {
        const decimal rate = 0.06m;
        var amount = Math.Round(income * rate, 2);
        return new TaxCalculationResult
        {
            TaxAmount = amount,
            EffectiveRate = 6m,
            SuggestedPaymentName = "УСН",
            Description =
                $"УСН (РБ): {income:N0}{BynCurrency.Suffix} выручки × 6% ≈ {amount:N2}{BynCurrency.Suffix}. Расходы в расчёт не входят. Срок — квартальная декларация.",
            Lines = [new TaxCalcLine { Name = "УСН (6% от выручки)", Amount = amount }]
        };
    }

    private static TaxCalculationResult CalculateOsnRb(TaxCalculatorInput input)
    {
        var profit = Math.Max(0, input.Income - input.Expenses);
        var isIp = input.TaxpayerKind == TaxpayerKind.IndividualEntrepreneur;
        var rate = isIp ? 0.16m : 0.20m;
        var ratePct = isIp ? 16m : 20m;
        var taxName = isIp ? "Подоходный (ИП)" : "Налог на прибыль";
        var incomeTax = Math.Round(profit * rate, 2);

        var lines = new List<TaxCalcLine>
        {
            new() { Name = taxName, Amount = incomeTax }
        };

        decimal vat = 0;
        if (!isIp && input.Income > 0)
        {
            vat = Math.Round(input.Income * 20m / 120m, 2);
            lines.Insert(0, new TaxCalcLine { Name = "НДС (оценка, 20/120)", Amount = vat });
        }

        var total = incomeTax + vat;
        return new TaxCalculationResult
        {
            TaxAmount = total,
            EffectiveRate = input.Income > 0 ? Math.Round(total / input.Income * 100, 1) : 0,
            SuggestedPaymentName = isIp ? "Подоходный" : "Налог на прибыль",
            Description =
                $"ОСН: доход {input.Income:N0} − расход {input.Expenses:N0} = база {profit:N0}{BynCurrency.Suffix}. " +
                $"{taxName} {ratePct}% ≈ {incomeTax:N2}{BynCurrency.Suffix}" +
                (vat > 0 ? $", НДС (оценка) ≈ {vat:N2}{BynCurrency.Suffix}" : "") + ".",
            Lines = lines
        };
    }

    private static TaxCalculationResult CalculateNpd(TaxCalculatorInput input)
    {
        var fromFl = input.IncomeFromIndividuals;
        var fromYur = input.IncomeFromLegalEntities;
        if (fromFl <= 0 && fromYur <= 0 && input.Income > 0)
            fromYur = input.Income;

        if (fromFl <= 0 && fromYur <= 0)
            return new TaxCalculationResult { Description = "Укажите доход от физлиц и/или юрлиц" };

        var taxFl = Math.Round(fromFl * 0.04m, 2);
        var taxYur = Math.Round(fromYur * 0.08m, 2);
        var total = taxFl + taxYur;
        var revenue = fromFl + fromYur;

        var lines = new List<TaxCalcLine>();
        if (taxFl > 0) lines.Add(new TaxCalcLine { Name = "НПД 4% (физлица)", Amount = taxFl });
        if (taxYur > 0) lines.Add(new TaxCalcLine { Name = "НПД 8% (юрлица/ИП)", Amount = taxYur });

        return new TaxCalculationResult
        {
            TaxAmount = total,
            EffectiveRate = revenue > 0 ? Math.Round(total / revenue * 100, 1) : 0,
            SuggestedPaymentName = "НПД",
            Description =
                $"НПД: {fromFl:N0}{BynCurrency.Suffix} от физлиц × 4% + {fromYur:N0}{BynCurrency.Suffix} от юрлиц × 8% ≈ {total:N2}{BynCurrency.Suffix}. " +
                "Для отчётности используйте приложение «Профдоход».",
            Lines = lines
        };
    }

    private static TaxCalculationResult CalculateUnified(decimal fixedAmount)
    {
        if (fixedAmount <= 0)
            return new TaxCalculationResult { Description = "Укажите сумму единого налога за период (из справочника МНС)" };

        return new TaxCalculationResult
        {
            TaxAmount = Math.Round(fixedAmount, 2),
            EffectiveRate = 0,
            SuggestedPaymentName = "Единый налог",
            Description = $"Единый налог (фиксированная сумма): {fixedAmount:N2}{BynCurrency.Suffix} за период. Ставка зависит от вида деятельности и населённого пункта.",
            Lines = [new TaxCalcLine { Name = "Единый налог", Amount = Math.Round(fixedAmount, 2) }]
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

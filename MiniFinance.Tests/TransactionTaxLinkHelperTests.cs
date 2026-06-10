using MiniFinance.Data.Models;
using MiniFinance.Services;
using Xunit;

namespace MiniFinance.Tests;

public class TransactionTaxLinkHelperTests
{
    [Fact]
    public void Income_usn_suggests_accrued_tax()
    {
        var line = new TransactionTaxLine
        {
            TransactionId = 1,
            Date = new DateTime(2026, 1, 15),
            Description = "Оплата от клиента",
            Category = "Выручка",
            Amount = 10_000m,
            Treatment = TransactionTaxTreatment.TaxableIncome,
            AccruedTax = 600m,
            RateLabel = "6%",
            Note = "УСН"
        };

        var suggestions = TransactionTaxLinkHelper.GetSuggestions(line, TaxSystem.USN, TaxpayerKind.LegalEntity);

        Assert.Single(suggestions);
        Assert.Equal("УСН", suggestions[0].PaymentType);
        Assert.Equal(600m, suggestions[0].Amount);
    }

    [Fact]
    public void Payroll_suggests_fszn()
    {
        var line = new TransactionTaxLine
        {
            TransactionId = 2,
            Date = new DateTime(2026, 2, 10),
            Description = "Зарплата Иванов",
            Category = "Зарплата",
            Amount = -5000m,
            Treatment = TransactionTaxTreatment.Excluded,
            Note = "Расход не влияет на УСН"
        };

        var suggestions = TransactionTaxLinkHelper.GetSuggestions(line, TaxSystem.USN, TaxpayerKind.LegalEntity);

        Assert.Single(suggestions);
        Assert.Equal("ФСЗН", suggestions[0].PaymentType);
        Assert.Equal(1700m, suggestions[0].Amount);
    }

    [Fact]
    public void Tax_payment_line_has_no_suggestions()
    {
        var line = new TransactionTaxLine
        {
            TransactionId = 3,
            Treatment = TransactionTaxTreatment.TaxPayment,
            Category = "Налоги",
            Amount = -100m
        };

        var suggestions = TransactionTaxLinkHelper.GetSuggestions(line, TaxSystem.USN, TaxpayerKind.LegalEntity);

        Assert.Empty(suggestions);
    }
}

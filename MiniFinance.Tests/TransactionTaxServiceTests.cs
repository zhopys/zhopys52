using MiniFinance.Data.Models;
using MiniFinance.Services;
using Xunit;

namespace MiniFinance.Tests;

public class TransactionTaxServiceTests
{
    [Fact]
    public void IsExcludedFromTaxBase_RecognizesTaxPayments()
    {
        var svc = new TransactionTaxService(null!, null!);
        Assert.True(svc.IsExcludedFromTaxBase("Налоги", "УСН квартал"));
        Assert.True(svc.IsExcludedFromTaxBase("Прочее", "внутренний перевод"));
        Assert.False(svc.IsExcludedFromTaxBase("Выручка", "Оплата от клиента"));
    }

    [Fact]
    public void IsLegalEntityCounterparty_DetectsUnpAndKeywords()
    {
        var svc = new TransactionTaxService(null!, null!);
        Assert.True(svc.IsLegalEntityCounterparty("Иванов", "123456789"));
        Assert.True(svc.IsLegalEntityCounterparty("ООО «БелТорг»", null));
        Assert.False(svc.IsLegalEntityCounterparty("Иванов И.И.", null));
    }

    [Theory]
    [InlineData(TaxSystem.USN, 10000, 0, 600)]
    [InlineData(TaxSystem.NPD, 1000, 0, 40)]
    public void Calculate_MatchesPerTransactionRates(TaxSystem system, decimal income, decimal expenses, decimal expectedMin)
    {
        var result = TaxCalculatorHelper.Calculate(new TaxCalculatorInput
        {
            System = system,
            Income = income,
            Expenses = expenses,
            IncomeFromIndividuals = system == TaxSystem.NPD ? income : 0
        });
        Assert.True(result.TaxAmount >= expectedMin);
    }
}

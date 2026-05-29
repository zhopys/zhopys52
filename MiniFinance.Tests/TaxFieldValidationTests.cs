using MiniFinance.Data.Models;
using MiniFinance.Services;
using Xunit;

namespace MiniFinance.Tests;

public class TaxFieldValidationTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("123456789", true)]
    [InlineData("12345678", false)]
    [InlineData("1234567890", false)]
    [InlineData("12345678a", false)]
    public void ValidateUnp_cases(string unp, bool expected)
    {
        var (ok, _) = TaxFieldValidation.ValidateUnp(unp);
        Assert.Equal(expected, ok);
    }

    [Fact]
    public void ValidateTaxPayment_requires_custom_name_for_other_type()
    {
        var errors = TaxFieldValidation.ValidateTaxPayment("Другое", "", 100, DateTime.Today.AddDays(7));
        Assert.True(errors.HasErrors);
        Assert.Equal("Укажите название платежа", errors.Get("customName"));
    }

    [Fact]
    public void ValidatePartialPayment_rejects_amount_over_remaining()
    {
        var errors = TaxFieldValidation.ValidatePartialPayment(150, 100, null);
        Assert.Contains("остаток", errors.Get("amount"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCalculatorInput_requires_tax_system()
    {
        var input = new TaxCalculatorInput { Income = 1000 };
        var (ok, error) = TaxFieldValidation.ValidateCalculatorInput(input, null);
        Assert.False(ok);
        Assert.Contains("систему налогообложения", error, StringComparison.OrdinalIgnoreCase);
    }
}

public class TaxCalculatorHelperTests
{
    [Fact]
    public void CalculateUsn_includes_fszn_when_requested()
    {
        var result = TaxCalculatorHelper.Calculate(new TaxCalculatorInput
        {
            System = TaxSystem.USN,
            Income = 1000,
            IncludeFsznEstimate = true
        });

        Assert.Equal(60, result.Lines.First(l => l.Name.Contains("УСН")).Amount);
        Assert.Equal(350, result.Lines.First(l => l.Name.Contains("ФСЗН")).Amount);
        Assert.Equal(410, result.TaxAmount);
    }

    [Fact]
    public void CalculateNpd_splits_rates()
    {
        var result = TaxCalculatorHelper.Calculate(new TaxCalculatorInput
        {
            System = TaxSystem.NPD,
            IncomeFromIndividuals = 1000,
            IncomeFromLegalEntities = 1000
        });

        Assert.Equal(40, result.Lines.First(l => l.Name.Contains("4%")).Amount);
        Assert.Equal(80, result.Lines.First(l => l.Name.Contains("8%")).Amount);
        Assert.Equal(120, result.TaxAmount);
    }
}

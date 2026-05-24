using MiniFinance.Services;
using Xunit;

namespace MiniFinance.Tests;

public class AuthFieldValidationTests
{
    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("", false)]
    [InlineData("bad@", false)]
    [InlineData("user@x.co", true)]
    public void ValidateEmail_cases(string email, bool expected)
    {
        var (ok, _) = AuthFieldValidation.ValidateEmail(email);
        Assert.Equal(expected, ok);
    }

    [Theory]
    [InlineData("short1", false)]
    [InlineData("longenough1", true)]
    [InlineData("nodigits", false)]
    public void ValidatePassword_cases(string pwd, bool expected)
    {
        var (ok, _) = AuthFieldValidation.ValidatePassword(pwd);
        Assert.Equal(expected, ok);
    }

    [Theory]
    [InlineData("123456", true)]
    [InlineData("12345", false)]
    [InlineData("12ab56", false)]
    public void ValidateTwoFactorCode_cases(string code, bool expected)
    {
        var (ok, _) = AuthFieldValidation.ValidateTwoFactorCode(code);
        Assert.Equal(expected, ok);
    }
}

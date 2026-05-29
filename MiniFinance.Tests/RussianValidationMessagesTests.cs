using Microsoft.AspNetCore.Components.Forms;
using MiniFinance.Data.Models;
using MiniFinance.Services;
using Xunit;

namespace MiniFinance.Tests;

public class RussianValidationMessagesTests
{
    [Fact]
    public void Translate_number_field_message()
    {
        var message = RussianValidationMessages.Translate("The Amount field must be a number.");
        Assert.Equal("Поле «Сумма» должно быть числом.", message);
    }

    [Fact]
    public void Translate_required_field_message()
    {
        var message = RussianValidationMessages.Translate("The Email field is required.");
        Assert.Equal("Поле «Email» обязательно для заполнения.", message);
    }

    [Fact]
    public void Translate_uses_display_attribute()
    {
        var tx = new Transaction();
        var field = FieldIdentifier.Create(() => tx.Amount);
        var message = RussianValidationMessages.Translate("The Amount field must be a number.", field);
        Assert.Equal("Поле «Сумма» должно быть числом.", message);
    }

    [Fact]
    public void Translate_keeps_russian_message()
    {
        const string original = "Укажите сумму";
        Assert.Equal(original, RussianValidationMessages.Translate(original));
    }
}

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MiniFinance.Services;

namespace MiniFinance.Components.Shared.Validation;

public class RussianInputNumber<TValue> : InputNumber<TValue>
{
    protected override bool TryParseValueFromString(
        string? value,
        [MaybeNullWhen(false)] out TValue result,
        [NotNullWhen(false)] out string? validationErrorMessage)
    {
        var ok = base.TryParseValueFromString(value, out result, out validationErrorMessage);
        if (!ok && validationErrorMessage != null)
            validationErrorMessage = RussianValidationMessages.Translate(validationErrorMessage, FieldIdentifier);
        return ok;
    }
}

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Forms;

namespace MiniFinance.Services;

public static class RussianValidationMessages
{
    private static readonly Dictionary<string, string> FieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Amount"] = "Сумма",
        ["Date"] = "Дата",
        ["Description"] = "Описание",
        ["Category"] = "Категория",
        ["Email"] = "Email",
        ["Password"] = "Пароль",
        ["OldPassword"] = "Текущий пароль",
        ["NewPassword"] = "Новый пароль",
        ["ConfirmPassword"] = "Подтверждение пароля",
        ["TwoFactorCode"] = "Код",
        ["RecoveryCode"] = "Код восстановления",
        ["Code"] = "Код",
        ["NewEmail"] = "Email",
        ["FirstName"] = "Имя",
        ["LastName"] = "Фамилия",
        ["Phone"] = "Телефон",
        ["Name"] = "Название",
        ["Budget"] = "Бюджет",
        ["ROI"] = "ROI",
    };

    public static string Translate(string? message, FieldIdentifier? field = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message ?? string.Empty;

        if (message.Any(c => c is >= '\u0400' and <= '\u04FF'))
            return message;

        var displayName = field.HasValue ? GetDisplayName(field.Value) : null;
        var text = message.Trim();

        if (TryMatch(@"^The (.+?) field must be a number\.$", text, out var numberField))
            return $"Поле «{ResolveFieldName(numberField, displayName)}» должно быть числом.";

        if (TryMatch(@"^The (.+?) field must be a date\.$", text, out var dateField))
            return $"Поле «{ResolveFieldName(dateField, displayName)}» должно быть датой.";

        if (TryMatch(@"^The (.+?) field is required\.$", text, out var requiredField))
            return $"Поле «{ResolveFieldName(requiredField, displayName)}» обязательно для заполнения.";

        if (TryMatch(@"^The (.+?) field is not a valid e-mail address\.$", text, out var emailField))
            return $"Поле «{ResolveFieldName(emailField, displayName)}» — некорректный email.";

        if (TryMatch(@"^The (.+?) field is not a valid fully-qualified http, https, or ftp URL\.$", text, out var urlField))
            return $"Поле «{ResolveFieldName(urlField, displayName)}» — некорректный URL.";

        if (TryMatch(@"^The field (.+?) must match the regular expression '(.+?)'\.$", text, out var regexField))
            return $"Поле «{ResolveFieldName(regexField, displayName)}» имеет неверный формат.";

        if (TryMatch(@"^The field (.+?) must be a string with a minimum length of (\d+) and a maximum length of (\d+)\.$",
                text, out var minMaxField))
            return $"Поле «{ResolveFieldName(minMaxField, displayName)}» — неверная длина.";

        if (TryMatch(@"^The field (.+?) must be a string or array type with a minimum length of '(\d+)'\.$",
                text, out var minLenField))
            return $"Поле «{ResolveFieldName(minLenField, displayName)}» слишком короткое.";

        if (TryMatch(@"^The field (.+?) must be a string or array type with a maximum length of '(\d+)'\.$",
                text, out var maxLenField))
            return $"Поле «{ResolveFieldName(maxLenField, displayName)}» слишком длинное.";

        if (TryMatch(@"^The field (.+?) must be between (\d+) and (\d+) - characters\.$", text, out var charRangeField))
            return $"Поле «{ResolveFieldName(charRangeField, displayName)}» — неверная длина.";

        if (TryMatch(@"^The (.+?) field must be between (.+?) and (.+?)\.$", text, out var rangeField))
            return $"Поле «{ResolveFieldName(rangeField, displayName)}» вне допустимого диапазона.";

        if (TryMatch(@"^The (.+?) must be between (.+?) and (.+?)\.$", text, out var rangeField2))
            return $"Поле «{ResolveFieldName(rangeField2, displayName)}» вне допустимого диапазона.";

        if (TryMatch(@"^The (.+?) field is not valid\.$", text, out var invalidField))
            return $"Поле «{ResolveFieldName(invalidField, displayName)}» заполнено неверно.";

        if (TryMatch(@"^(.+?) is not valid\.$", text, out var invalidShort))
            return $"Поле «{ResolveFieldName(invalidShort, displayName)}» заполнено неверно.";

        if (text.Contains("' and '", StringComparison.OrdinalIgnoreCase)
            && text.Contains("do not match", StringComparison.OrdinalIgnoreCase))
            return "Значения не совпадают.";

        if (text.Contains("Compare", StringComparison.OrdinalIgnoreCase)
            && text.Contains("match", StringComparison.OrdinalIgnoreCase))
            return "Значения не совпадают.";

        return text switch
        {
            "A non-empty request body is required." => "Заполните обязательные поля.",
            "The supplied value is invalid." => "Некорректное значение.",
            "The value is invalid." => "Некорректное значение.",
            _ => text
        };
    }

    public static string GetDisplayName(FieldIdentifier field)
    {
        if (field.Model == null)
            return ResolveFieldName(field.FieldName, null);

        var property = field.Model.GetType().GetProperty(field.FieldName);
        if (property != null)
        {
            var display = property.GetCustomAttribute<DisplayAttribute>();
            if (!string.IsNullOrWhiteSpace(display?.Name))
                return display!.Name!;
            if (!string.IsNullOrWhiteSpace(display?.GetName()))
                return display!.GetName()!;
        }

        return ResolveFieldName(field.FieldName, null);
    }

    private static string ResolveFieldName(string? englishName, string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
            return preferred;

        if (string.IsNullOrWhiteSpace(englishName))
            return "Поле";

        if (FieldNames.TryGetValue(englishName, out var mapped))
            return mapped;

        return englishName;
    }

    private static bool TryMatch(string pattern, string text, out string fieldName)
    {
        fieldName = string.Empty;
        var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;

        fieldName = match.Groups[1].Value;
        return true;
    }
}

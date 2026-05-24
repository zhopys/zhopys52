using System.Text.RegularExpressions;

namespace MiniFinance.Services;

public static class AuthFieldValidation
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    public const int MaxEmailLength = 256;
    public const int MaxNameLength = 100;
    public const int MaxPhoneLength = 30;
    public const int TwoFactorCodeLength = 6;

    private static readonly Regex EmailRegex = new(
        @"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? "" : email.Trim().ToLowerInvariant();

    public static (bool Ok, string? Error) ValidateEmail(string? email)
    {
        var e = NormalizeEmail(email);
        if (string.IsNullOrEmpty(e))
            return (false, "Email обязателен");
        if (e.Length > MaxEmailLength)
            return (false, $"Email не длиннее {MaxEmailLength} символов");
        if (e.Contains("..", StringComparison.Ordinal))
            return (false, "Некорректный email");
        if (!EmailRegex.IsMatch(e))
            return (false, "Некорректный формат email");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidatePassword(string? password, bool required = true)
    {
        if (string.IsNullOrEmpty(password))
            return required ? (false, "Пароль обязателен") : (true, null);
        if (password.Length < MinPasswordLength)
            return (false, $"Пароль должен быть не менее {MinPasswordLength} символов");
        if (password.Length > MaxPasswordLength)
            return (false, $"Пароль не длиннее {MaxPasswordLength} символов");
        if (!password.Any(char.IsDigit))
            return (false, "Пароль должен содержать хотя бы одну цифру");
        if (password.Any(char.IsControl))
            return (false, "Пароль содержит недопустимые символы");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateTwoFactorCode(string? code)
    {
        var c = (code ?? "").Replace(" ", "").Replace("-", "");
        if (c.Length != TwoFactorCodeLength)
            return (false, $"Код должен содержать {TwoFactorCodeLength} цифр");
        if (!c.All(char.IsDigit))
            return (false, "Код должен содержать только цифры");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidatePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return (true, null);
        var p = phone.Trim();
        if (p.Length > MaxPhoneLength)
            return (false, "Слишком длинный номер телефона");
        if (!Regex.IsMatch(p, @"^[\d\s\-\+\(\)]+$"))
            return (false, "Телефон: только цифры, +, скобки и пробелы");
        return (true, null);
    }
}

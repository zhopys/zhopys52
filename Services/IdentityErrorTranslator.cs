using Microsoft.AspNetCore.Identity;

namespace MiniFinance.Services;

public static class IdentityErrorTranslator
{
    public static string Join(IEnumerable<IdentityError> errors) =>
        string.Join(" ", errors.Select(Translate));

    public static string Translate(IdentityError e) => e.Code switch
    {
        "DuplicateUserName" or "DuplicateEmail" => "Этот email уже используется.",
        "InvalidEmail" => "Некорректный email.",
        "PasswordTooShort" => e.Description,
        "PasswordRequiresDigit" => "Пароль должен содержать хотя бы одну цифру.",
        "PasswordRequiresNonAlphanumeric" => "Пароль должен содержать спецсимвол.",
        "PasswordRequiresUpper" => "Пароль должен содержать заглавную букву.",
        "PasswordRequiresLower" => "Пароль должен содержать строчную букву.",
        "InvalidToken" => "Ссылка недействительна или устарела.",
        _ => string.IsNullOrWhiteSpace(e.Description) || e.Description.All(c => c < '\u0400')
            ? e.Description
            : e.Description
    };
}

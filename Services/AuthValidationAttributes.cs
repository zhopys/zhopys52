using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Services;

public sealed class ValidAuthEmailAttribute : ValidationAttribute
{
    public ValidAuthEmailAttribute() => ErrorMessage = "Некорректный email";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var (ok, error) = AuthFieldValidation.ValidateEmail(value as string);
        return ok
            ? ValidationResult.Success
            : new ValidationResult(error ?? ErrorMessage, [validationContext.MemberName!]);
    }
}

public sealed class ValidAuthPasswordAttribute : ValidationAttribute
{
    public ValidAuthPasswordAttribute() => ErrorMessage = "Некорректный пароль";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var (ok, error) = AuthFieldValidation.ValidatePassword(value as string);
        return ok
            ? ValidationResult.Success
            : new ValidationResult(error ?? ErrorMessage, [validationContext.MemberName!]);
    }
}

public sealed class ValidTwoFactorCodeAttribute : ValidationAttribute
{
    public ValidTwoFactorCodeAttribute() => ErrorMessage = "Некорректный код";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var (ok, error) = AuthFieldValidation.ValidateTwoFactorCode(value as string);
        return ok
            ? ValidationResult.Success
            : new ValidationResult(error ?? ErrorMessage, [validationContext.MemberName!]);
    }
}

public sealed class ValidPhoneAttribute : ValidationAttribute
{
    public ValidPhoneAttribute() => ErrorMessage = "Некорректный телефон";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var (ok, error) = AuthFieldValidation.ValidatePhone(value as string);
        return ok
            ? ValidationResult.Success
            : new ValidationResult(error ?? ErrorMessage, [validationContext.MemberName!]);
    }
}

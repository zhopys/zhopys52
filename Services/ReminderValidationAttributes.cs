using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Services;

public sealed class ValidReminderNameAttribute : ValidationAttribute
{
    public ValidReminderNameAttribute() => ErrorMessage = "Некорректное название";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var (ok, error) = ReminderFieldValidation.ValidateName(value as string);
        return ok ? ValidationResult.Success : new ValidationResult(error ?? ErrorMessage);
    }
}

public sealed class ValidReminderAmountAttribute : ValidationAttribute
{
    public ValidReminderAmountAttribute() => ErrorMessage = "Некорректная сумма";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var amount = value is decimal d ? d : 0m;
        var (ok, error) = ReminderFieldValidation.ValidateAmount(amount);
        return ok ? ValidationResult.Success : new ValidationResult(error ?? ErrorMessage);
    }
}

public sealed class ValidReminderDateAttribute : ValidationAttribute
{
    public ValidReminderDateAttribute() => ErrorMessage = "Некорректная дата";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var date = value is DateTime dt ? dt : default;
        var (ok, error) = ReminderFieldValidation.ValidateDate(date);
        return ok ? ValidationResult.Success : new ValidationResult(error ?? ErrorMessage);
    }
}

public sealed class ValidReminderNotifyDaysAttribute : ValidationAttribute
{
    public ValidReminderNotifyDaysAttribute() => ErrorMessage = "Некорректный срок напоминания";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var days = value is int i ? i : 0;
        var (ok, error) = ReminderFieldValidation.ValidateNotifyDays(days);
        return ok ? ValidationResult.Success : new ValidationResult(error ?? ErrorMessage);
    }
}

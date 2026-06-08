namespace MiniFinance.Services;

public static class ReminderFieldValidation
{
    public const int MaxNameLength = 200;
    public const int MaxCategoryLength = 100;
    public const int MaxNotesLength = 500;
    public const decimal MinAmount = 0.01m;
    public const decimal MaxAmount = 1_000_000m;
    public const int MinNotifyDays = 0;
    public const int MaxNotifyDays = 90;

    public static (bool Ok, string? Error) ValidateName(string? name)
    {
        var n = name?.Trim() ?? "";
        if (string.IsNullOrEmpty(n))
            return (false, "Укажите название");
        if (n.Length > MaxNameLength)
            return (false, $"Название не длиннее {MaxNameLength} символов");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateAmount(decimal amount)
    {
        if (amount < MinAmount)
            return (false, $"Сумма должна быть не менее {MinAmount:N2}");
        if (amount > MaxAmount)
            return (false, $"Сумма не больше {MaxAmount:N0}");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateDate(DateTime date)
    {
        if (date == default)
            return (false, "Укажите дату");
        if (date.Year < 2000 || date.Year > 2100)
            return (false, "Некорректная дата");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateNotifyDays(int days)
    {
        if (days < MinNotifyDays || days > MaxNotifyDays)
            return (false, $"Напоминание за {MinNotifyDays}–{MaxNotifyDays} дней до срока");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateNotes(string? notes)
    {
        if (notes is { Length: > MaxNotesLength })
            return (false, $"Заметки не длиннее {MaxNotesLength} символов");
        return (true, null);
    }
}

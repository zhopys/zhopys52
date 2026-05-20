namespace MiniFinance.Services;

public static class AppRoles
{
    public const string Owner = "Owner";
    public const string Accountant = "Accountant";
    public const string Manager = "Manager";

    public static readonly string[] All = [Owner, Accountant, Manager];
}

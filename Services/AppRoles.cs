namespace MiniFinance.Services;

/// <summary>Роли MiniFinance: администратор, бухгалтер, налоговый специалист.</summary>
public static class AppRoles
{
    public const string Administrator = "Administrator";
    public const string Accountant = "Accountant";
    public const string TaxSpecialist = "TaxSpecialist";

    /// <summary>Устаревшие имена ролей (миграция из ранних версий).</summary>
    public const string LegacyOwner = "Owner";
    public const string LegacyManager = "Manager";

    public static readonly string[] All = [Administrator, Accountant, TaxSpecialist];

    public static readonly IReadOnlyDictionary<string, string> LegacyRoleMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [LegacyOwner] = Administrator,
            [LegacyManager] = TaxSpecialist,
        };

    public static string GetDisplayName(string role) => role switch
    {
        Administrator => "Администратор",
        Accountant => "Бухгалтер",
        TaxSpecialist => "Налоговый специалист",
        LegacyOwner => "Администратор",
        LegacyManager => "Налоговый специалист",
        _ => role
    };

    public static string GetBadgeClass(string role) => role switch
    {
        Administrator => "badge-role-admin",
        Accountant => "badge-role-accountant",
        TaxSpecialist => "badge-role-tax",
        LegacyOwner => "badge-role-admin",
        LegacyManager => "badge-role-tax",
        _ => "badge-accent"
    };

    public static string GetPrimaryRole(IEnumerable<string> roles)
    {
        var list = roles.ToList();
        if (list.Contains(Administrator)) return Administrator;
        if (list.Contains(Accountant)) return Accountant;
        if (list.Contains(TaxSpecialist)) return TaxSpecialist;
        foreach (var r in list)
        {
            if (LegacyRoleMap.TryGetValue(r, out var mapped))
                return mapped;
        }
        return list.FirstOrDefault() ?? Accountant;
    }
}

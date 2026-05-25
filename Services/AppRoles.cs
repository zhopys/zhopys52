using System.Security.Claims;

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

    public static string NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return Accountant;
        return LegacyRoleMap.TryGetValue(role.Trim(), out var mapped) ? mapped : role.Trim();
    }

    public static bool IsValidRole(string? role) =>
        !string.IsNullOrWhiteSpace(role) && All.Contains(NormalizeRole(role), StringComparer.OrdinalIgnoreCase);

    public static string GetDisplayName(string role) => NormalizeRole(role) switch
    {
        Administrator => "Администратор",
        Accountant => "Бухгалтер",
        TaxSpecialist => "Налоговый специалист",
        _ => role
    };

    public static string GetBadgeClass(string role) => NormalizeRole(role) switch
    {
        Administrator => "badge-role-admin",
        Accountant => "badge-role-accountant",
        TaxSpecialist => "badge-role-tax",
        _ => "badge-accent"
    };

    public static string GetDescription(string role) => NormalizeRole(role) switch
    {
        Administrator =>
            "Полный доступ: финансы, налоги, отчёты, настройки организации, команда и роли.",
        Accountant =>
            "Операции, импорт, контрагенты, долги, проекты, календарь и отчёты. Без раздела «Налоги» и управления пользователями.",
        TaxSpecialist =>
            "Налоги, формулы, плановые платежи, PDF для налоговика и отчёты. Без списка транзакций и финансовых разделов.",
        _ => "Роль не назначена"
    };

    public static string GetPrimaryRole(IEnumerable<string> roles)
    {
        var normalized = roles.Select(NormalizeRole).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (normalized.Contains(Administrator)) return Administrator;
        if (normalized.Contains(Accountant)) return Accountant;
        if (normalized.Contains(TaxSpecialist)) return TaxSpecialist;
        return normalized.FirstOrDefault() ?? Accountant;
    }

    public static bool IsAdministratorRole(string role) =>
        NormalizeRole(role) == Administrator;

    public static bool HasAdminAccess(ClaimsPrincipal user) =>
        user.IsInRole(Administrator) || user.IsInRole(LegacyOwner);

    public static bool HasFinanceAccess(ClaimsPrincipal user) =>
        HasAdminAccess(user) || user.IsInRole(Accountant);

    public static bool HasTaxAccess(ClaimsPrincipal user) =>
        HasAdminAccess(user) || user.IsInRole(TaxSpecialist) || user.IsInRole(LegacyManager);

    public static bool HasReportsAccess(ClaimsPrincipal user) =>
        HasFinanceAccess(user) || HasTaxAccess(user);

    public static bool HasAdminAccess(IEnumerable<string> roles) =>
        roles.Any(r => IsAdministratorRole(r) || r.Equals(LegacyOwner, StringComparison.OrdinalIgnoreCase));

    public static bool HasFinanceAccess(IEnumerable<string> roles) =>
        HasAdminAccess(roles) || roles.Any(r => NormalizeRole(r) == Accountant);

    public static bool HasTaxAccess(IEnumerable<string> roles) =>
        HasAdminAccess(roles) || roles.Any(r =>
        {
            var n = NormalizeRole(r);
            return n == TaxSpecialist;
        }) || roles.Any(r => r.Equals(LegacyManager, StringComparison.OrdinalIgnoreCase));

    public static bool HasReportsAccess(IEnumerable<string> roles) =>
        HasFinanceAccess(roles) || HasTaxAccess(roles);
}

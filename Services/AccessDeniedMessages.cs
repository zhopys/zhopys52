namespace MiniFinance.Services;

public static class AccessDeniedMessages
{
    public const string Default = "У вашей учётной записи нет прав для этого раздела.";

    public static string ForPolicy(string? policyName) => policyName switch
    {
        AuthorizationPolicies.AdministratorOnly or AuthorizationPolicies.CanManageUsers =>
            "Раздел «Команда и пользователи» доступен только администратору.",
        AuthorizationPolicies.CanManageSettings =>
            "Настройки организации доступны только администратору.",
        AuthorizationPolicies.CanAccessFinances =>
            "Финансовые разделы (операции, контрагенты, долги, категории, календарь) недоступны налоговому специалисту. Используйте раздел «Налоги».",
        AuthorizationPolicies.CanImport =>
            "Импорт данных доступен администратору и бухгалтеру.",
        AuthorizationPolicies.CanViewReports =>
            "Отчёты и аналитика недоступны вашей роли.",
        AuthorizationPolicies.CanManageTaxes =>
            "Налоговый раздел недоступен бухгалтеру. Обратитесь к налоговому специалисту или администратору.",
        "OwnerOnly" =>
            "Раздел доступен только администратору.",
        _ => Default
    };

    public static string ForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Default;
        path = path.Split('?', '#')[0].ToLowerInvariant();

        if (path is "/team") return ForPolicy(AuthorizationPolicies.CanManageUsers);
        if (path is "/settings") return ForPolicy(AuthorizationPolicies.CanManageSettings);
        if (path is "/taxes") return ForPolicy(AuthorizationPolicies.CanManageTaxes);
        if (path is "/import") return ForPolicy(AuthorizationPolicies.CanImport);
        if (path is "/reports" or "/analytics") return ForPolicy(AuthorizationPolicies.CanViewReports);
        if (path is "/transactions" or "/counterparties" or "/debts" or "/categories"
            or "/reminders" or "/payment-calendar" or "/cash-forecast" or "/projects")
            return ForPolicy(AuthorizationPolicies.CanAccessFinances);

        return Default;
    }
}

namespace MiniFinance.Services;

public static class AuthorizationPolicies
{
    public const string AdministratorOnly = "AdministratorOnly";
    public const string CanManageUsers = "CanManageUsers";
    public const string CanManageSettings = "CanManageSettings";
    public const string CanAccessFinances = "CanAccessFinances";
    public const string CanImport = "CanImport";
    public const string CanViewReports = "CanViewReports";
    public const string CanManageTaxes = "CanManageTaxes";
}

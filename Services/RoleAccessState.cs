using System.Security.Claims;

namespace MiniFinance.Services;

/// <summary>Снимок прав и роли из claims текущего пользователя.</summary>
public readonly record struct RoleAccessSnapshot(
    bool IsAuthenticated,
    bool IsAdministrator,
    bool CanManageUsers,
    bool CanManageSettings,
    bool CanAccessFinances,
    bool CanManageTaxes,
    bool CanViewReports,
    string PrimaryRole)
{
    public static RoleAccessSnapshot FromPrincipal(ClaimsPrincipal user)
    {
        var authenticated = user.Identity?.IsAuthenticated == true;
        if (!authenticated)
            return new RoleAccessSnapshot(false, false, false, false, false, false, false, "");

        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var primary = AppRoles.GetPrimaryRole(roles);
        var isAdmin = AppRoles.HasAdminAccess(user);

        return new RoleAccessSnapshot(
            true,
            isAdmin,
            isAdmin,
            isAdmin,
            AppRoles.HasFinanceAccess(user),
            AppRoles.HasTaxAccess(user),
            AppRoles.HasReportsAccess(user),
            primary);
    }
}

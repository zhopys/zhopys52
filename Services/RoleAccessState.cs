using System.Security.Claims;

namespace MiniFinance.Services;

/// <summary>Снимок прав и роли из claims текущего пользователя.</summary>
public readonly record struct RoleAccessSnapshot(
    bool IsAuthenticated,
    bool IsAdministrator,
    bool CanAccessFinances,
    bool CanManageTaxes,
    bool CanViewReports,
    string PrimaryRole)
{
    public static RoleAccessSnapshot FromPrincipal(ClaimsPrincipal user)
    {
        var authenticated = user.Identity?.IsAuthenticated == true;
        if (!authenticated)
            return new RoleAccessSnapshot(false, false, false, false, false, "");

        var isAdmin = user.IsInRole(AppRoles.Administrator);
        var canFinance = isAdmin || user.IsInRole(AppRoles.Accountant);
        var canTax = isAdmin || user.IsInRole(AppRoles.TaxSpecialist);
        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value);
        var primary = AppRoles.GetPrimaryRole(roles);

        return new RoleAccessSnapshot(authenticated, isAdmin, canFinance, canTax, canFinance || canTax, primary);
    }
}

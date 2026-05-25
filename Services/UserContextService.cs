using Microsoft.AspNetCore.Identity;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class UserContextService : IUserContextService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDataScopeService _dataScope;

    public UserContextService(UserManager<ApplicationUser> userManager, IDataScopeService dataScope)
    {
        _userManager = userManager;
        _dataScope = dataScope;
    }

    public async Task<UserContext> GetContextAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        var roles = user != null
            ? (await _userManager.GetRolesAsync(user)).ToList()
            : new List<string>();

        NormalizeLegacyRoles(roles);

        if (roles.Count == 0 && string.IsNullOrWhiteSpace(user?.WorkspaceOwnerUserId))
            roles.Add(AppRoles.Administrator);

        var dataUserId = await _dataScope.GetDataOwnerUserIdAsync(userId);

        return new UserContext
        {
            UserId = userId,
            DataUserId = dataUserId,
            Roles = roles,
            Department = user?.Department,
            ActiveProjectId = user?.ActiveProjectId
        };
    }

    public bool IsAdministrator(UserContext ctx) => ctx.IsAdministrator;
    public bool IsAccountant(UserContext ctx) => ctx.IsAccountant;
    public bool IsTaxSpecialist(UserContext ctx) => ctx.IsTaxSpecialist;
    public bool CanManageUsers(UserContext ctx) => ctx.IsAdministrator;
    public bool CanAccessSettings(UserContext ctx) => ctx.IsAdministrator;
    public bool CanAccessFinances(UserContext ctx) => ctx.IsAdministrator || ctx.IsAccountant;
    public bool CanImport(UserContext ctx) => ctx.IsAdministrator || ctx.IsAccountant;
    public bool CanManageTransactions(UserContext ctx) => CanAccessFinances(ctx);
    public bool CanApproveTransactions(UserContext ctx) => ctx.IsAdministrator || ctx.IsAccountant;
    public bool CanViewReports(UserContext ctx) =>
        ctx.IsAdministrator || ctx.IsAccountant || ctx.IsTaxSpecialist;
    public bool CanManageTaxes(UserContext ctx) => ctx.IsAdministrator || ctx.IsTaxSpecialist;
    public bool IsTaxSpecialistOnly(UserContext ctx) =>
        ctx.IsTaxSpecialist && !ctx.IsAdministrator && !ctx.IsAccountant;

    public IQueryable<Transaction> FilterTransactionsForRole(IQueryable<Transaction> q, UserContext ctx) =>
        CanAccessFinances(ctx) ? q : q.Where(_ => false);

    private static void NormalizeLegacyRoles(List<string> roles)
    {
        for (var i = 0; i < roles.Count; i++)
            roles[i] = AppRoles.NormalizeRole(roles[i]);

        var distinct = roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        roles.Clear();
        roles.AddRange(distinct);
    }
}

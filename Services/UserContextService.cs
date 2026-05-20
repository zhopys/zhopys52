using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class UserContextService : IUserContextService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserContextService(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<UserContext> GetContextAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        var roles = user != null
            ? (await _userManager.GetRolesAsync(user)).ToList()
            : new List<string>();

        if (roles.Count == 0)
            roles.Add(AppRoles.Owner);

        return new UserContext
        {
            UserId = userId,
            Roles = roles,
            Department = user?.Department,
            ActiveProjectId = user?.ActiveProjectId
        };
    }

    public bool CanAccessSettings(UserContext ctx) => ctx.IsOwner;
    public bool CanManageTransactions(UserContext ctx) => ctx.IsOwner || ctx.IsManager;
    public bool CanApproveTransactions(UserContext ctx) => ctx.IsOwner;
    public bool CanViewReports(UserContext ctx) => ctx.IsOwner || ctx.IsAccountant || ctx.IsManager;

    public IQueryable<Transaction> FilterTransactionsForRole(IQueryable<Transaction> q, UserContext ctx)
    {
        if (ctx.IsOwner || ctx.IsAccountant)
            return q;

        if (ctx.IsManager)
        {
            if (ctx.ActiveProjectId.HasValue)
                return q.Where(t => t.ProjectId == ctx.ActiveProjectId);

            if (!string.IsNullOrWhiteSpace(ctx.Department))
                return q.Where(t => t.Project != null && t.Project.Department == ctx.Department);
        }

        return q;
    }
}

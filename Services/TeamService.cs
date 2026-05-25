using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;

namespace MiniFinance.Services;

public class TeamService : ITeamService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IDataScopeService _dataScope;
    private readonly IUserRoleRefreshRegistry _roleRefreshRegistry;

    public TeamService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IDataScopeService dataScope,
        IUserRoleRefreshRegistry roleRefreshRegistry)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _dataScope = dataScope;
        _roleRefreshRegistry = roleRefreshRegistry;
    }

    public async Task<List<TeamMemberDto>> ListMembersAsync(string actorUserId)
    {
        var ownerId = await _dataScope.GetDataOwnerUserIdAsync(actorUserId);
        var users = await _userManager.Users
            .Where(u => u.Id == ownerId || u.WorkspaceOwnerUserId == ownerId)
            .OrderBy(u => u.Id == ownerId ? 0 : 1)
            .ThenBy(u => u.Email)
            .ToListAsync();

        var list = new List<TeamMemberDto>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            var role = AppRoles.GetPrimaryRole(roles);
            var isOwner = u.Id == ownerId;
            list.Add(new TeamMemberDto
            {
                Id = u.Id,
                Email = u.Email ?? "",
                FirstName = u.FirstName,
                LastName = u.LastName,
                Department = u.Department,
                Role = role,
                IsWorkspaceOwner = isOwner,
                CanChangeRole = !isOwner,
                CreatedAt = u.CreatedAt
            });
        }
        return list;
    }

    public async Task<(bool Ok, string? Error)> InviteMemberAsync(InviteMemberRequest request, string invitedByUserId)
    {
        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return (false, "Укажите email");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return (false, "Пароль не короче 8 символов");

        var role = AppRoles.NormalizeRole(request.Role);
        if (!AppRoles.IsValidRole(role))
            return (false, "Некорректная роль");

        if (await _userManager.FindByEmailAsync(email) != null)
            return (false, "Пользователь с таким email уже есть");

        await EnsureRolesExistAsync();

        var workspaceOwnerId = await _dataScope.GetDataOwnerUserIdAsync(invitedByUserId);

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim(),
            Department = string.IsNullOrWhiteSpace(request.Department) ? null : request.Department.Trim(),
            CreatedAt = DateTime.UtcNow,
            WorkspaceOwnerUserId = workspaceOwnerId
        };

        var created = await _userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            return (false, string.Join("; ", created.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, role);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> SetRoleAsync(string userId, string role, string actorUserId)
    {
        role = AppRoles.NormalizeRole(role);
        if (!AppRoles.IsValidRole(role))
            return (false, "Некорректная роль");

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return (false, "Пользователь не найден");

        var ownerId = await _dataScope.GetDataOwnerUserIdAsync(actorUserId);
        if (user.Id != ownerId && user.WorkspaceOwnerUserId != ownerId)
            return (false, "Пользователь не входит в вашу организацию");

        if (user.Id == ownerId && role != AppRoles.Administrator)
            return (false, "Владелец организации всегда остаётся администратором");

        if (userId == actorUserId && role != AppRoles.Administrator)
        {
            if (await CountWorkspaceAdminsAsync(ownerId) <= 1)
                return (false, "Нельзя снять с себя роль администратора — в организации должен остаться администратор");
        }

        if (await _userManager.IsInRoleAsync(user, AppRoles.Administrator) && role != AppRoles.Administrator)
        {
            if (await CountWorkspaceAdminsAsync(ownerId) <= 1)
                return (false, "В организации должен остаться хотя бы один администратор");
        }

        var current = await _userManager.GetRolesAsync(user);
        var normalizedCurrent = current.Select(AppRoles.NormalizeRole).Distinct().ToList();
        if (normalizedCurrent.Count == 1 && normalizedCurrent[0] == role)
            return (true, null);

        await _userManager.RemoveFromRolesAsync(user, current);
        await _userManager.AddToRoleAsync(user, role);
        await _userManager.UpdateSecurityStampAsync(user);
        await _roleRefreshRegistry.NotifyRoleChangedAsync(userId);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> SetDepartmentAsync(string userId, string? department, string actorUserId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return (false, "Пользователь не найден");

        var ownerId = await _dataScope.GetDataOwnerUserIdAsync(actorUserId);
        if (user.Id != ownerId && user.WorkspaceOwnerUserId != ownerId)
            return (false, "Пользователь не входит в вашу организацию");

        user.Department = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
        var result = await _userManager.UpdateAsync(user);
        return result.Succeeded ? (true, null) : (false, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<(bool Ok, string? Error)> RemoveMemberAsync(string userId, string actorUserId)
    {
        if (userId == actorUserId)
            return (false, "Нельзя удалить свою учётную запись");

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return (false, "Пользователь не найден");

        var ownerId = await _dataScope.GetDataOwnerUserIdAsync(actorUserId);
        if (user.Id == ownerId)
            return (false, "Нельзя удалить владельца организации");
        if (user.WorkspaceOwnerUserId != ownerId)
            return (false, "Пользователь не входит в вашу организацию");

        if (await _userManager.IsInRoleAsync(user, AppRoles.Administrator)
            && await CountWorkspaceAdminsAsync(ownerId) <= 1)
            return (false, "Нельзя удалить последнего администратора организации");

        var result = await _userManager.DeleteAsync(user);
        return result.Succeeded ? (true, null) : (false, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    private async Task EnsureRolesExistAsync()
    {
        foreach (var role in AppRoles.All)
        {
            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    private async Task<int> CountWorkspaceAdminsAsync(string ownerId)
    {
        var users = await _userManager.Users
            .Where(u => u.Id == ownerId || u.WorkspaceOwnerUserId == ownerId)
            .ToListAsync();

        var count = 0;
        foreach (var u in users)
        {
            if (await _userManager.IsInRoleAsync(u, AppRoles.Administrator)
                || await _userManager.IsInRoleAsync(u, AppRoles.LegacyOwner))
                count++;
        }
        return count;
    }
}

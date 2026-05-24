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
            .OrderBy(u => u.Email)
            .ToListAsync();

        var list = new List<TeamMemberDto>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            var role = ResolvePrimaryRole(roles);
            list.Add(new TeamMemberDto
            {
                Id = u.Id,
                Email = u.Email ?? "",
                FirstName = u.FirstName,
                LastName = u.LastName,
                Department = u.Department,
                Role = role,
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
        if (!AppRoles.All.Contains(request.Role))
            return (false, "Некорректная роль");
        if (request.Role == AppRoles.Administrator)
            return (false, "Нельзя пригласить второго администратора через эту форму. Смените роль после создания.");

        if (await _userManager.FindByEmailAsync(email) != null)
            return (false, "Пользователь с таким email уже есть");

        foreach (var role in AppRoles.All)
        {
            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole(role));
        }

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

        await _userManager.AddToRoleAsync(user, request.Role);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> SetRoleAsync(string userId, string role, string actorUserId)
    {
        if (!AppRoles.All.Contains(role))
            return (false, "Некорректная роль");

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return (false, "Пользователь не найден");

        var ownerId = await _dataScope.GetDataOwnerUserIdAsync(actorUserId);
        if (user.Id != ownerId && user.WorkspaceOwnerUserId != ownerId)
            return (false, "Пользователь не входит в вашу организацию");

        if (userId == actorUserId && role != AppRoles.Administrator)
        {
            if (await CountInRoleAsync(AppRoles.Administrator) <= 1)
                return (false, "Нельзя снять с себя роль администратора — в системе должен остаться администратор");
        }

        if (await _userManager.IsInRoleAsync(user, AppRoles.Administrator) && role != AppRoles.Administrator)
        {
            if (await CountInRoleAsync(AppRoles.Administrator) <= 1)
                return (false, "В системе должен остаться хотя бы один администратор");
        }

        var current = await _userManager.GetRolesAsync(user);
        if (current.Count == 1 && current[0] == role)
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

        if (await _userManager.IsInRoleAsync(user, AppRoles.Administrator) && await CountInRoleAsync(AppRoles.Administrator) <= 1)
            return (false, "Нельзя удалить последнего администратора");

        var result = await _userManager.DeleteAsync(user);
        return result.Succeeded ? (true, null) : (false, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    private async Task<int> CountInRoleAsync(string role)
    {
        var users = await _userManager.GetUsersInRoleAsync(role);
        return users.Count;
    }

    private static string ResolvePrimaryRole(IList<string> roles)
    {
        if (roles.Contains(AppRoles.Administrator)) return AppRoles.Administrator;
        if (roles.Contains(AppRoles.Accountant)) return AppRoles.Accountant;
        if (roles.Contains(AppRoles.TaxSpecialist)) return AppRoles.TaxSpecialist;
        if (roles.Count > 0 && AppRoles.LegacyRoleMap.TryGetValue(roles[0], out var mapped))
            return mapped;
        return AppRoles.Accountant;
    }
}

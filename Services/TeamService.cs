using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;

namespace MiniFinance.Services;

public class TeamService : ITeamService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IDataScopeService _dataScope;

    public TeamService(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IDataScopeService dataScope)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _dataScope = dataScope;
    }

    public async Task<List<TeamMemberDto>> ListMembersAsync()
    {
        var users = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();
        var list = new List<TeamMemberDto>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            list.Add(new TeamMemberDto
            {
                Id = u.Id,
                Email = u.Email ?? "",
                FirstName = u.FirstName,
                LastName = u.LastName,
                Department = u.Department,
                Role = roles.FirstOrDefault() ?? AppRoles.Owner,
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
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 3)
            return (false, "Пароль не короче 3 символов");
        if (!AppRoles.All.Contains(request.Role))
            return (false, "Некорректная роль");

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

        if (userId == actorUserId && role != AppRoles.Owner)
        {
            var ownerCount = await CountInRoleAsync(AppRoles.Owner);
            if (ownerCount <= 1)
                return (false, "Нельзя снять с себя роль владельца — в системе должен остаться владелец");
        }

        if (await _userManager.IsInRoleAsync(user, AppRoles.Owner) && role != AppRoles.Owner)
        {
            if (await CountInRoleAsync(AppRoles.Owner) <= 1)
                return (false, "В системе должен остаться хотя бы один владелец");
        }

        var current = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, current);
        await _userManager.AddToRoleAsync(user, role);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> SetDepartmentAsync(string userId, string? department)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return (false, "Пользователь не найден");
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

        if (await _userManager.IsInRoleAsync(user, AppRoles.Owner) && await CountInRoleAsync(AppRoles.Owner) <= 1)
            return (false, "Нельзя удалить последнего владельца");

        var result = await _userManager.DeleteAsync(user);
        return result.Succeeded ? (true, null) : (false, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    private async Task<int> CountInRoleAsync(string role)
    {
        var users = await _userManager.GetUsersInRoleAsync(role);
        return users.Count;
    }
}

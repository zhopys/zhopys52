namespace MiniFinance.Services;

public sealed class TeamMemberDto
{
    public string Id { get; init; } = "";
    public string Email { get; init; } = "";
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Department { get; init; }
    public string Role { get; init; } = AppRoles.Owner;
    public DateTime? CreatedAt { get; init; }
}

public sealed class InviteMemberRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = AppRoles.Manager;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Department { get; set; }
}

public interface ITeamService
{
    Task<List<TeamMemberDto>> ListMembersAsync();
    Task<(bool Ok, string? Error)> InviteMemberAsync(InviteMemberRequest request, string invitedByUserId);
    Task<(bool Ok, string? Error)> SetRoleAsync(string userId, string role, string actorUserId);
    Task<(bool Ok, string? Error)> SetDepartmentAsync(string userId, string? department);
    Task<(bool Ok, string? Error)> RemoveMemberAsync(string userId, string actorUserId);
}

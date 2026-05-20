namespace MiniFinance.Services;

public interface IUserContextService
{
    Task<UserContext> GetContextAsync(string userId);
    bool CanAccessSettings(UserContext ctx);
    bool CanManageTransactions(UserContext ctx);
    bool CanApproveTransactions(UserContext ctx);
    bool CanViewReports(UserContext ctx);
    IQueryable<Data.Models.Transaction> FilterTransactionsForRole(IQueryable<Data.Models.Transaction> q, UserContext ctx);
}

public sealed class UserContext
{
    public string UserId { get; init; } = "";
    /// <summary>UserId владельца данных организации (для запросов к БД).</summary>
    public string DataUserId { get; init; } = "";
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public string? Department { get; init; }
    public int? ActiveProjectId { get; init; }
    public bool IsOwner => Roles.Contains(AppRoles.Owner);
    public bool IsAccountant => Roles.Contains(AppRoles.Accountant);
    public bool IsManager => Roles.Contains(AppRoles.Manager);
}

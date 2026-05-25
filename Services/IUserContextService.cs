namespace MiniFinance.Services;

public interface IUserContextService
{
    Task<UserContext> GetContextAsync(string userId);
    bool IsAdministrator(UserContext ctx);
    bool IsAccountant(UserContext ctx);
    bool IsTaxSpecialist(UserContext ctx);
    bool CanManageUsers(UserContext ctx);
    bool CanAccessSettings(UserContext ctx);
    bool CanAccessFinances(UserContext ctx);
    bool CanImport(UserContext ctx);
    bool CanManageTransactions(UserContext ctx);
    bool CanApproveTransactions(UserContext ctx);
    bool CanViewReports(UserContext ctx);
    bool CanManageTaxes(UserContext ctx);
    /// <summary>Только налоговый специалист (без финансовых разделов).</summary>
    bool IsTaxSpecialistOnly(UserContext ctx);
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

    public bool IsAdministrator => AppRoles.HasAdminAccess(Roles);
    public bool IsAccountant => Roles.Any(r => AppRoles.NormalizeRole(r) == AppRoles.Accountant);
    public bool IsTaxSpecialist => Roles.Any(r => AppRoles.NormalizeRole(r) == AppRoles.TaxSpecialist);
}

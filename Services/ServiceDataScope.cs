namespace MiniFinance.Services;

internal static class ServiceDataScope
{
    public static async Task<string> ResolveAsync(IDataScopeService scope, string userId) =>
        await scope.GetDataOwnerUserIdAsync(userId);
}

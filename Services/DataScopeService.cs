using Microsoft.AspNetCore.Identity;
using MiniFinance.Data;

namespace MiniFinance.Services;

public class DataScopeService : IDataScopeService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public DataScopeService(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<string> GetDataOwnerUserIdAsync(string currentUserId)
    {
        if (string.IsNullOrWhiteSpace(currentUserId))
            return currentUserId;

        var user = await _userManager.FindByIdAsync(currentUserId);
        if (user == null)
            return currentUserId;

        return string.IsNullOrWhiteSpace(user.WorkspaceOwnerUserId)
            ? currentUserId
            : user.WorkspaceOwnerUserId;
    }
}

using MiniFinance.Data;

namespace MiniFinance.Services;

public interface ITwoFactorService
{
    Task<bool> IsEnabledAsync(ApplicationUser user);
    Task<(bool Success, string? Error)> SetEnabledAsync(ApplicationUser user, bool enabled);
    Task<(bool Success, string? Error)> SendLoginCodeAsync(ApplicationUser user);
}

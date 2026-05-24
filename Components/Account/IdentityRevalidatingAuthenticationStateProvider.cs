using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MiniFinance.Data;
using MiniFinance.Services;

namespace MiniFinance.Components.Account;

/// <summary>
/// Проверяет security stamp и актуальность ролей; обновляет cookie/claims без выхода из системы.
/// </summary>
internal sealed class IdentityRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> options,
    IUserRoleRefreshRegistry roleRefreshRegistry,
    IHttpContextAccessor httpContextAccessor,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory), IDisposable
{
    private Guid? _registrationId;
    private string? _registeredUserId;

    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.GetUserAsync(authenticationState.User);
        if (user is null)
            return false;

        var userId = user.Id;
        EnsureRegistered(userId);

        if (!await ValidateSecurityStampAsync(userManager, authenticationState.User))
            return false;

        if (!RolesMatch(authenticationState.User, await userManager.GetRolesAsync(user)))
        {
            await RefreshSignInAndNotifyAsync();
        }

        return true;
    }

    private void EnsureRegistered(string userId)
    {
        if (_registrationId.HasValue && _registeredUserId == userId)
            return;

        if (_registrationId.HasValue && _registeredUserId is not null)
            roleRefreshRegistry.Unregister(_registeredUserId, _registrationId.Value);

        _registeredUserId = userId;
        _registrationId = roleRefreshRegistry.Register(userId, RefreshSignInAndNotifyAsync);
    }

    private async Task RefreshSignInAndNotifyAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated != true)
            return;

        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return;

        await signInManager.RefreshSignInAsync(user);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private async Task<bool> ValidateSecurityStampAsync(UserManager<ApplicationUser> userManager, ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
            return false;

        if (!userManager.SupportsUserSecurityStamp)
            return true;

        var principalStamp = principal.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType);
        var userStamp = await userManager.GetSecurityStampAsync(user);
        return principalStamp == userStamp;
    }

    private static bool RolesMatch(ClaimsPrincipal principal, IList<string> currentRoles)
    {
        var claimRoles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dbRoles = currentRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return claimRoles.SetEquals(dbRoles);
    }

    public void Dispose()
    {
        if (_registrationId.HasValue && _registeredUserId is not null)
            roleRefreshRegistry.Unregister(_registeredUserId, _registrationId.Value);
    }
}

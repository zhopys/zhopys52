using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using MiniFinance.Data;

namespace MiniFinance.Components.Account;

internal static class LoginEndpoint
{
    public static void MapLoginEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/Account/Login", HandleLoginAsync);
    }

    private static async Task<IResult> HandleLoginAsync(
        [FromServices] SignInManager<ApplicationUser> signInManager,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] bool? rememberMe,
        [FromForm] string? returnUrl,
        ILogger<Program> logger)
    {
        var persist = rememberMe == true;
        email = (email ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            return Results.Redirect(AccountUrls.Login(error: "empty", email: email));
        }

        var user = await userManager.FindByEmailAsync(email)
                   ?? await userManager.FindByNameAsync(email);

        if (user is null)
        {
            logger.LogWarning("Login failed: user not found for {Email}", email);
            return Results.Redirect(AccountUrls.Login(error: "invalid", email: email));
        }

        if (!user.EmailConfirmed)
        {
            return Results.Redirect(AccountUrls.Login(error: "unconfirmed", email: email));
        }

        var loginName = user.UserName ?? email;
        var result = await signInManager.PasswordSignInAsync(
            loginName, password, persist, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            logger.LogInformation("User {Email} logged in.", email);
            var target = AccountUrls.SanitizeReturnUrl(returnUrl) ?? "/";
            return Results.Redirect(target);
        }

        if (result.IsLockedOut)
        {
            return Results.Redirect("/Account/Lockout");
        }

        if (result.RequiresTwoFactor)
        {
            return Results.Redirect(QueryHelpers.AddQueryString("/Account/LoginWith2fa",
                new Dictionary<string, string?>
                {
                    ["returnUrl"] = returnUrl,
                    ["rememberMe"] = persist.ToString().ToLowerInvariant()
                }));
        }

        logger.LogWarning("Login failed for {Email}: {Result}", email, result.ToString());
        return Results.Redirect(AccountUrls.Login(error: "invalid", email: email));
    }
}

internal static class AccountUrls
{
    public static string Login(string? error = null, string? email = null, bool confirmed = false, string? returnUrl = null)
    {
        var q = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(error))
            q["error"] = error;
        if (!string.IsNullOrWhiteSpace(email))
            q["email"] = email;
        if (confirmed)
            q["confirmed"] = "1";
        if (!string.IsNullOrWhiteSpace(returnUrl))
            q["ReturnUrl"] = returnUrl;
        return QueryHelpers.AddQueryString("/login", q);
    }

    public static string? SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return null;
        if (!returnUrl.StartsWith('/'))
            return null;
        if (returnUrl.StartsWith("//", StringComparison.Ordinal))
            return null;
        return returnUrl;
    }
}

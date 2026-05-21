using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using MiniFinance.Data;

namespace MiniFinance.Components.Account;

internal static class LoginEndpoint
{
    public static void MapLoginEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/Account/Login", (string? returnUrl) =>
        {
            var q = new Dictionary<string, string?>();
            if (!string.IsNullOrWhiteSpace(returnUrl))
                q["ReturnUrl"] = returnUrl;
            return Results.Redirect(QueryHelpers.AddQueryString("/login", q));
        });

        endpoints.MapPost("/Account/Login", HandleLoginAsync).DisableAntiforgery();
    }

    private static async Task<IResult> HandleLoginAsync(
        HttpContext httpContext,
        [FromServices] SignInManager<ApplicationUser> signInManager,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] IAntiforgery antiforgery,
        [FromServices] ILogger<Program> logger)
    {
        if (!httpContext.Request.HasFormContentType)
            return Results.Redirect(AccountUrls.Login(error: "empty"));

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Login antiforgery validation failed");
            return Results.Redirect(AccountUrls.Login(error: "session_expired"));
        }

        var form = await httpContext.Request.ReadFormAsync();
        var email = (form["email"].ToString() ?? "").Trim().ToLowerInvariant();
        var password = form["password"].ToString() ?? "";
        var rememberMe = form["rememberMe"].ToString();
        var returnUrl = form["returnUrl"].ToString();
        var persist = rememberMe is "true" or "on";

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            return Results.Redirect(AccountUrls.Login(error: "empty", email: email));

        var user = await userManager.FindByEmailAsync(email)
                   ?? await userManager.FindByNameAsync(email);

        if (user is null)
        {
            logger.LogWarning("Login failed: user not found for {Email}", email);
            return Results.Redirect(AccountUrls.Login(error: "invalid", email: email));
        }

        if (!user.EmailConfirmed)
            return Results.Redirect(AccountUrls.Login(error: "unconfirmed", email: email));

        var loginName = user.UserName ?? email;
        var result = await signInManager.PasswordSignInAsync(
            loginName, password, persist, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            logger.LogInformation("User {Email} logged in.", email);
            var target = AccountUrls.SanitizeReturnUrl(returnUrl) ?? "/";
            return Results.Redirect(target);
        }

        if (result.IsLockedOut)
            return Results.Redirect("/Account/Lockout");

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

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using MiniFinance.Data;
using MiniFinance.Services;

namespace MiniFinance.Components.Account;

internal static class AccountDeleteEndpoint
{
    public static void MapAccountDeleteEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/Account/Delete", HandleDeleteAccountAsync)
            .RequireAuthorization()
            .DisableAntiforgery();
    }

    private static async Task<IResult> HandleDeleteAccountAsync(
        HttpContext httpContext,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] SignInManager<ApplicationUser> signInManager,
        [FromServices] IAccountProfileService accountProfile,
        [FromServices] IAntiforgery antiforgery,
        [FromServices] ILogger<Program> logger)
    {
        if (!httpContext.Request.HasFormContentType)
            return Results.Redirect(AccountDeleteUrls.AccountData(error: "empty"));

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Delete account antiforgery failed");
            return Results.Redirect(AccountDeleteUrls.AccountData(error: "session"));
        }

        var user = await userManager.GetUserAsync(httpContext.User);
        if (user == null)
            return Results.Redirect("/login");

        var password = httpContext.Request.Form["password"].ToString() ?? "";
        if (string.IsNullOrWhiteSpace(password))
            return Results.Redirect(AccountDeleteUrls.AccountData(error: "password"));

        var (success, error) = await accountProfile.DeleteAccountAsync(user.Id, password);
        if (!success)
        {
            logger.LogWarning("Delete account failed for {Email}: {Error}", user.Email, error);
            return Results.Redirect(AccountDeleteUrls.AccountData(error: "failed", detail: error));
        }

        await signInManager.SignOutAsync();
        logger.LogInformation("Account deleted for {Email}", user.Email);
        return Results.Redirect("/login?deleted=1");
    }
}

internal static class AccountDeleteUrls
{
    public static string AccountData(string? error = null, string? detail = null)
    {
        var q = new Dictionary<string, string?> { ["tab"] = "data" };
        if (!string.IsNullOrWhiteSpace(error))
            q["deleteError"] = error;
        if (!string.IsNullOrWhiteSpace(detail))
            q["deleteDetail"] = detail;
        return QueryHelpers.AddQueryString("/account", q);
    }
}

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using MiniFinance.Data;
using MiniFinance.Services;

namespace MiniFinance.Components.Account;

internal static class TwoFactorLoginEndpoint
{
    public static void MapTwoFactorLoginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/Account/LoginWith2fa/Verify", HandleLoginWith2faAsync).DisableAntiforgery();
        endpoints.MapPost("/Account/LoginWith2fa/Resend", HandleResendAsync).DisableAntiforgery();
    }

    private static async Task<IResult> HandleLoginWith2faAsync(
        HttpContext httpContext,
        [FromServices] SignInManager<ApplicationUser> signInManager,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] IAntiforgery antiforgery,
        [FromServices] ILogger<Program> logger)
    {
        if (!httpContext.Request.HasFormContentType)
            return Results.Redirect(To2faPage(error: "session"));

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "2FA login antiforgery validation failed");
            return Results.Redirect(To2faPage(error: "session"));
        }

        var form = await httpContext.Request.ReadFormAsync();
        var code = (form["twoFactorCode"].ToString() ?? "").Replace(" ", "").Replace("-", "");
        var returnUrl = form["returnUrl"].ToString();

        var codeCheck = AuthFieldValidation.ValidateTwoFactorCode(code);
        if (!codeCheck.Ok)
            return Results.Redirect(To2faPage(error: "invalid_code", returnUrl: returnUrl));

        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            logger.LogWarning("2FA sign-in attempted without pending user");
            return Results.Redirect(AccountUrls.Login(error: "session_expired"));
        }

        var result = await signInManager.TwoFactorSignInAsync(
            TokenOptions.DefaultEmailProvider,
            code,
            isPersistent: false,
            rememberClient: false);

        if (result.Succeeded)
        {
            var userId = await userManager.GetUserIdAsync(user);
            logger.LogInformation("User {UserId} logged in with 2FA.", userId);
            return Results.Redirect(AccountUrls.SanitizeReturnUrl(returnUrl) ?? "/");
        }

        if (result.IsLockedOut)
            return Results.Redirect("/Account/Lockout");

        logger.LogWarning("Invalid 2FA code for user {UserId}", user.Id);
        return Results.Redirect(To2faPage(error: "invalid_code", returnUrl: returnUrl));
    }

    private static async Task<IResult> HandleResendAsync(
        HttpContext httpContext,
        [FromServices] SignInManager<ApplicationUser> signInManager,
        [FromServices] ITwoFactorService twoFactor,
        [FromServices] IAntiforgery antiforgery,
        [FromServices] ILogger<Program> logger)
    {
        if (!httpContext.Request.HasFormContentType)
            return Results.Redirect(To2faPage(error: "session"));

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "2FA resend antiforgery validation failed");
            return Results.Redirect(To2faPage(error: "session"));
        }

        var form = await httpContext.Request.ReadFormAsync();
        var returnUrl = form["returnUrl"].ToString();

        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
            return Results.Redirect(AccountUrls.Login(error: "session_expired"));

        var (sent, sendError) = await twoFactor.SendLoginCodeAsync(user);
        if (!sent)
        {
            logger.LogWarning("2FA resend failed for {UserId}: {Error}", user.Id, sendError);
            return Results.Redirect(To2faPage(error: "send_failed", returnUrl: returnUrl));
        }

        return Results.Redirect(To2faPage(sent: true, returnUrl: returnUrl));
    }

    private static string To2faPage(
        string? error = null,
        bool sent = false,
        string? returnUrl = null)
    {
        var q = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(error))
            q["error"] = error;
        if (sent)
            q["sent"] = "1";
        if (!string.IsNullOrWhiteSpace(returnUrl))
            q["returnUrl"] = returnUrl;
        return QueryHelpers.AddQueryString("/Account/LoginWith2fa", q);
    }
}

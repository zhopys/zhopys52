using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using MiniFinance.Data;

namespace MiniFinance.Components.Account;

internal static class ConfirmEmailEndpoint
{
    public static void MapConfirmEmailEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/Account/ConfirmEmail", HandleConfirmEmailAsync);
    }

    private static async Task<IResult> HandleConfirmEmailAsync(
        [FromQuery] string? userId,
        [FromQuery] string? code,
        [FromQuery] string? returnUrl,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<Program> logger)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
        {
            return Results.Redirect(AccountUrls.Login(error: "invalid_link"));
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Results.Redirect(AccountUrls.Login(error: "user_not_found"));
        }

        var email = user.Email ?? "";

        if (!user.EmailConfirmed)
        {
            try
            {
                var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
                var result = await userManager.ConfirmEmailAsync(user, token);
                if (!result.Succeeded)
                {
                    logger.LogWarning("Email confirm failed for {UserId}: {Errors}",
                        userId, string.Join(", ", result.Errors.Select(e => e.Description)));
                    return Results.Redirect(AccountUrls.Login(error: "confirm_failed", email: email));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Invalid confirmation code for {UserId}", userId);
                return Results.Redirect(AccountUrls.Login(error: "invalid_code", email: email));
            }

            user = await userManager.FindByIdAsync(userId) ?? user;
        }

        if (!user.EmailConfirmed)
        {
            return Results.Redirect(AccountUrls.Login(error: "confirm_failed", email: email));
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        logger.LogInformation("User {Email} confirmed email and signed in.", email);

        if (!string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
        {
            return Results.Redirect(returnUrl);
        }

        return Results.Redirect("/");
    }
}

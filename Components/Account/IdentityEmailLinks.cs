using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace MiniFinance.Components.Account;

internal static class IdentityEmailLinks
{
    public static string BuildConfirmEmailUrl(string baseUri, string userId, string confirmationToken, string? returnUrl = null)
    {
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(confirmationToken));
        var query = new Dictionary<string, string?>
        {
            ["userId"] = userId,
            ["code"] = code
        };
        if (!string.IsNullOrWhiteSpace(returnUrl))
            query["returnUrl"] = returnUrl;

        return QueryHelpers.AddQueryString(NormalizePath(baseUri, "/Account/ConfirmEmail"), query);
    }

    public static string BuildResetPasswordUrl(string baseUri, string resetToken)
    {
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(resetToken));
        return QueryHelpers.AddQueryString(
            NormalizePath(baseUri, "/Account/ResetPassword"),
            new Dictionary<string, string?> { ["code"] = code });
    }

    private static string NormalizePath(string baseUri, string path)
    {
        var root = baseUri.TrimEnd('/');
        return $"{root}{path}";
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace MiniFinance.Services;

/// <summary>Перенаправление на страницу отказа с русским текстом по имени политики.</summary>
public sealed class RussianAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && authorizeResult.AuthorizationFailure is not null)
        {
            var policyName = context.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>()?.Policy
                ?? "unknown";
            var message = AccessDeniedMessages.ForPolicy(policyName);
            context.Response.Redirect($"/Account/AccessDenied?policy={Uri.EscapeDataString(policyName)}&message={Uri.EscapeDataString(message)}");
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

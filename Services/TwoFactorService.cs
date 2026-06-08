using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MiniFinance.Data;

namespace MiniFinance.Services;

public sealed class TwoFactorService : ITwoFactorService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INotificationEmailService _email;
    private readonly SmtpSettings _smtp;
    private readonly ILogger<TwoFactorService> _logger;

    public TwoFactorService(
        UserManager<ApplicationUser> userManager,
        INotificationEmailService email,
        IOptions<SmtpSettings> smtp,
        ILogger<TwoFactorService> logger)
    {
        _userManager = userManager;
        _email = email;
        _smtp = smtp.Value;
        _logger = logger;
    }

    public Task<bool> IsEnabledAsync(ApplicationUser user) =>
        _userManager.GetTwoFactorEnabledAsync(user);

    public async Task<(bool Success, string? Error)> SetEnabledAsync(ApplicationUser user, bool enabled)
    {
        if (enabled)
        {
            var email = await _userManager.GetEmailAsync(user);
            if (string.IsNullOrWhiteSpace(email))
                return (false, "Укажите email в профиле, чтобы включить 2FA.");

            if (!await _userManager.IsEmailConfirmedAsync(user))
                return (false, "Подтвердите email перед включением 2FA.");

            if (!IsSmtpConfigured())
                return (false, "Почта не настроена. Обратитесь к администратору.");
        }

        var result = await _userManager.SetTwoFactorEnabledAsync(user, enabled);
        if (!result.Succeeded)
            return (false, string.Join(" ", result.Errors.Select(e => e.Description)));

        if (enabled)
            await _userManager.ResetAuthenticatorKeyAsync(user);

        _logger.LogInformation("User {UserId} set 2FA to {Enabled}", user.Id, enabled);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SendLoginCodeAsync(ApplicationUser user)
    {
        var email = await _userManager.GetEmailAsync(user);
        if (string.IsNullOrWhiteSpace(email))
            return (false, "Email не указан.");

        if (!IsSmtpConfigured())
            return (false, "Почта не настроена. Обратитесь к администратору.");

        var code = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
        if (string.IsNullOrWhiteSpace(code))
        {
            _logger.LogWarning("Empty 2FA token generated for user {UserId}", user.Id);
            return (false, "Не удалось сформировать код. Попробуйте позже.");
        }

        var safeCode = HtmlEncoder.Default.Encode(code.Trim());
        var html = $"""
            <div style="font-family:Inter,sans-serif;max-width:480px;margin:0 auto;padding:24px">
              <h2 style="margin:0 0 12px">Код для входа в MiniFinance</h2>
              <p style="color:#555">Введите код на странице входа (действует 10 минут):</p>
              <p style="font-size:28px;font-weight:700;letter-spacing:4px;margin:16px 0">{safeCode}</p>
              <p style="font-size:12px;color:#888">Если вы не пытались войти, проигнорируйте это письмо.</p>
            </div>
            """;

        try
        {
            await _email.SendRawEmailAsync(email, "Код входа — MiniFinance", html);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send 2FA code to {Email}", email);
            return (false, "Не удалось отправить код на email.");
        }
    }

    private bool IsSmtpConfigured() => _smtp.IsConfigured;
}

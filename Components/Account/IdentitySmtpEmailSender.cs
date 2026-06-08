using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MiniFinance.Data;
using MiniFinance.Services;

namespace MiniFinance.Components.Account;

internal sealed class IdentitySmtpEmailSender : IEmailSender<ApplicationUser>
{
    private readonly INotificationEmailService _email;
    private readonly SmtpSettings _smtp;
    private readonly AppSettings _app;
    private readonly ILogger<IdentitySmtpEmailSender> _logger;

    public IdentitySmtpEmailSender(
        INotificationEmailService email,
        IOptions<SmtpSettings> smtp,
        IOptions<AppSettings> app,
        ILogger<IdentitySmtpEmailSender> logger)
    {
        _email = email;
        _smtp = smtp.Value;
        _app = app.Value;
        _logger = logger;
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(email, "Подтверждение email — MiniFinance", WrapBody(
            "Подтвердите регистрацию",
            "Нажмите кнопку, чтобы подтвердить адрес email и войти в MiniFinance.",
            confirmationLink,
            "Подтвердить email"));

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, "Сброс пароля — MiniFinance", WrapBody(
            "Сброс пароля",
            "Если вы не запрашивали сброс, проигнорируйте это письмо.",
            resetLink,
            "Сбросить пароль"));

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(email, "Код сброса пароля — MiniFinance",
            EmailTemplateBuilder.WrapSimpleCard(
                "Код сброса пароля",
                $"Код для сброса пароля: {resetCode}",
                $"{_app.PublicUrl.TrimEnd('/')}/Account/Login",
                "Перейти ко входу",
                "MiniFinance"));

    private static string WrapBody(string title, string text, string link, string buttonText) =>
        EmailTemplateBuilder.WrapIdentityContent(title, text, link, buttonText);

    private async Task SendAsync(string email, string subject, string htmlBody)
    {
        if (!_smtp.IsConfigured)
        {
            _logger.LogWarning("SMTP not configured, email to {Email} not sent", email);
            throw new InvalidOperationException("Почта не настроена. Обратитесь к администратору.");
        }

        await _email.SendRawEmailAsync(email, subject, htmlBody);
    }
}

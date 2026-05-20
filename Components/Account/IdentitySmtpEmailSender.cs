using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MiniFinance.Data;
using MiniFinance.Services;

namespace MiniFinance.Components.Account;

internal sealed class IdentitySmtpEmailSender : IEmailSender<ApplicationUser>
{
    private readonly INotificationEmailService _email;
    private readonly SmtpSettings _smtp;
    private readonly ILogger<IdentitySmtpEmailSender> _logger;

    public IdentitySmtpEmailSender(
        INotificationEmailService email,
        IOptions<SmtpSettings> smtp,
        ILogger<IdentitySmtpEmailSender> logger)
    {
        _email = email;
        _smtp = smtp.Value;
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
            $"<p>Код для сброса пароля: <strong>{HtmlEncoder.Default.Encode(resetCode)}</strong></p>");

    private static string WrapBody(string title, string text, string link, string buttonText)
    {
        var safeHref = HtmlEncoder.Default.Encode(link);
        return $@"
<p>Здравствуйте!</p>
<p><strong>{HtmlEncoder.Default.Encode(title)}</strong></p>
<p>{HtmlEncoder.Default.Encode(text)}</p>
<p style=""margin:24px 0"">
  <a href=""{safeHref}"" style=""display:inline-block;background:#7c3aed;color:#fff;padding:12px 24px;border-radius:8px;text-decoration:none;font-weight:600"">{HtmlEncoder.Default.Encode(buttonText)}</a>
</p>
<p class=""text-muted"" style=""font-size:12px;color:#666"">Или скопируйте ссылку:<br/><span style=""word-break:break-all"">{safeHref}</span></p>";
    }

    private async Task SendAsync(string email, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_smtp.Host) || string.IsNullOrWhiteSpace(_smtp.FromEmail))
        {
            _logger.LogWarning("SMTP not configured, email to {Email} not sent", email);
            throw new InvalidOperationException("Почта не настроена. Обратитесь к администратору.");
        }

        await _email.SendRawEmailAsync(email, subject, htmlBody);
    }
}

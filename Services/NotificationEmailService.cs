using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MiniFinance.Services;

public class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "MiniFinance";
}

public class NotificationSettings
{
    public int CheckIntervalHours { get; set; } = 24;
}

public class NotificationEmailService : INotificationEmailService
{
    private readonly SmtpSettings _smtp;
    private readonly ILogger<NotificationEmailService> _logger;

    public NotificationEmailService(IOptions<SmtpSettings> smtp, ILogger<NotificationEmailService> logger)
    {
        _smtp = smtp.Value;
        _logger = logger;
    }

    public async Task SendUpcomingPaymentNotificationAsync(string userEmail, string paymentName, decimal amount, DateTime dueDate, string paymentType, int daysUntilDue)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_smtp.Host) || string.IsNullOrWhiteSpace(_smtp.FromEmail))
            {
                _logger.LogWarning("SMTP not configured, skipping email notification to {Email}", userEmail);
                return;
            }

            var typeLabel = paymentType == "reminder" ? "платёж" : "налог";
            var body = $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background:#f5f0ff;font-family:system-ui,sans-serif'>
<table width='100%' cellpadding='0' cellspacing='0'><tr><td align='center' style='padding:40px 0'>
<table width='500' cellpadding='0' cellspacing='0' style='background:#fff;border-radius:12px;overflow:shadow 0 2px 8px rgba(0,0,0,0.1)'>
<tr><td style='background:#7c3aed;padding:24px;text-align:center'>
<h1 style='color:#fff;margin:0;font-size:22px'>MiniFinance</h1>
</td></tr>
<tr><td style='padding:32px 24px'>
<h2 style='margin:0 0 16px;color:#1a1a1a;font-size:18px'>Напоминание о платеже</h2>
<p style='color:#555;font-size:15px;line-height:1.6'>
До срока оплаты <strong>{typeLabel}</strong> осталось <strong style='color:#7c3aed'>{daysUntilDue} дн.</strong>
</p>
<table style='width:100%;border-collapse:collapse;margin-top:16px'>
<tr><td style='padding:8px 0;color:#888'>Название</td><td style='padding:8px 0;color:#1a1a1a;font-weight:600'>{paymentName}</td></tr>
<tr><td style='padding:8px 0;color:#888'>Сумма</td><td style='padding:8px 0;color:#1a1a1a;font-weight:600'>{amount:N2}</td></tr>
<tr><td style='padding:8px 0;color:#888'>Срок</td><td style='padding:8px 0;color:#1a1a1a;font-weight:600'>{dueDate:dd.MM.yyyy}</td></tr>
</table>
</td></tr>
<tr><td style='padding:0 24px 24px;text-align:center'>
<a href='https://localhost:7275' style='display:inline-block;background:#7c3aed;color:#fff;padding:12px 32px;border-radius:8px;text-decoration:none;font-weight:600'>Открыть MiniFinance</a>
</td></tr>
</table>
</td></tr></table>
</body>
</html>";

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_smtp.FromName, _smtp.FromEmail));
            message.To.Add(new MailboxAddress("", userEmail));
            message.Subject = $"MiniFinance: {typeLabel} «{paymentName}» — {daysUntilDue} дн. до срока";

            var bodyBuilder = new BodyBuilder { HtmlBody = body };
            message.Body = bodyBuilder.ToMessageBody();

            await SendMessageAsync(message);

            _logger.LogInformation("Notification email sent to {Email}: {PaymentName}", userEmail, paymentName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification email to {Email}", userEmail);
        }
    }

    public async Task SendTestEmailAsync(string userEmail)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_smtp.Host) || string.IsNullOrWhiteSpace(_smtp.FromEmail))
            {
                _logger.LogWarning("SMTP not configured, cannot send test email");
                return;
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_smtp.FromName, _smtp.FromEmail));
            message.To.Add(new MailboxAddress("", userEmail));
            message.Subject = "MiniFinance — Тестовое письмо";

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = @"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background:#f5f0ff;font-family:system-ui,sans-serif'>
<table width='100%' cellpadding='0' cellspacing='0'><tr><td align='center' style='padding:40px 0'>
<table width='400' cellpadding='0' cellspacing='0' style='background:#fff;border-radius:12px;overflow:shadow 0 2px 8px rgba(0,0,0,0.1)'>
<tr><td style='background:#7c3aed;padding:24px;text-align:center'>
<h1 style='color:#fff;margin:0;font-size:22px'>MiniFinance</h1>
</td></tr>
<tr><td style='padding:32px 24px;text-align:center'>
<h2 style='margin:0 0 8px;color:#1a1a1a'>Тестовое письмо</h2>
<p style='color:#555'>Email-уведомления настроены корректно!</p>
</td></tr>
</table>
</td></tr></table>
</body>
</html>"
            };
            message.Body = bodyBuilder.ToMessageBody();

            await SendMessageAsync(message);

            _logger.LogInformation("Test email sent to {Email}", userEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send test email to {Email}", userEmail);
            throw;
        }
    }

    public async Task SendRawEmailAsync(string userEmail, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_smtp.Host) || string.IsNullOrWhiteSpace(_smtp.FromEmail))
        {
            _logger.LogWarning("SMTP not configured, skipping email to {Email}", userEmail);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtp.FromName, _smtp.FromEmail));
        message.To.Add(new MailboxAddress("", userEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        await SendMessageAsync(message);
    }

    private async Task SendMessageAsync(MimeMessage message)
    {
        using var client = new SmtpClient();
        await client.ConnectAsync(_smtp.Host, _smtp.Port, GetSecureSocketOptions());
        if (!string.IsNullOrWhiteSpace(_smtp.Username))
            await client.AuthenticateAsync(_smtp.Username, _smtp.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private SecureSocketOptions GetSecureSocketOptions() =>
        _smtp.Port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            _ => _smtp.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None
        };

    public async Task<int> SendAllUpcomingNotificationsAsync(IEnumerable<(string Email, string Name, decimal Amount, DateTime DueDate, string Type, int DaysUntil)> items)
    {
        var sent = 0;
        foreach (var item in items)
        {
            try
            {
                await SendUpcomingPaymentNotificationAsync(item.Email, item.Name, item.Amount, item.DueDate, item.Type, item.DaysUntil);
                sent++;
            }
            catch { /* already logged */ }
        }
        return sent;
    }
}
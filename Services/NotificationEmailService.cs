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
    private readonly AppSettings _app;
    private readonly ILogger<NotificationEmailService> _logger;

    public NotificationEmailService(
        IOptions<SmtpSettings> smtp,
        IOptions<AppSettings> app,
        ILogger<NotificationEmailService> logger)
    {
        _smtp = smtp.Value;
        _app = app.Value;
        _logger = logger;
    }

    public async Task SendUpcomingPaymentNotificationAsync(
        string userEmail,
        string paymentName,
        decimal amount,
        DateTime dueDate,
        string paymentType,
        int daysUntilDue)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_smtp.Host) || string.IsNullOrWhiteSpace(_smtp.FromEmail))
            {
                _logger.LogWarning("SMTP not configured, skipping email notification to {Email}", userEmail);
                return;
            }

            var template = EmailTemplateBuilder.BuildPaymentReminder(new EmailTemplateBuilder.PaymentReminderModel(
                paymentName,
                amount,
                dueDate,
                paymentType,
                daysUntilDue,
                _app.PublicUrl));

            var message = CreateMessage(userEmail, template.Subject, template.HtmlBody, template.TextBody);
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

            var template = EmailTemplateBuilder.BuildTestEmail(_app.PublicUrl);
            var message = CreateMessage(userEmail, template.Subject, template.HtmlBody, template.TextBody);
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

        var message = CreateMessage(userEmail, subject, htmlBody, null);
        await SendMessageAsync(message);
    }

    public async Task<int> SendAllUpcomingNotificationsAsync(
        IEnumerable<(string Email, string Name, decimal Amount, DateTime DueDate, string Type, int DaysUntil)> items)
    {
        var sent = 0;
        foreach (var item in items)
        {
            try
            {
                await SendUpcomingPaymentNotificationAsync(
                    item.Email, item.Name, item.Amount, item.DueDate, item.Type, item.DaysUntil);
                sent++;
            }
            catch
            {
                // already logged
            }
        }

        return sent;
    }

    private MimeMessage CreateMessage(string userEmail, string subject, string htmlBody, string? textBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtp.FromName, _smtp.FromEmail));
        message.To.Add(new MailboxAddress("", userEmail));
        message.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = htmlBody };
        if (!string.IsNullOrWhiteSpace(textBody))
            builder.TextBody = textBody;
        message.Body = builder.ToMessageBody();
        return message;
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
}

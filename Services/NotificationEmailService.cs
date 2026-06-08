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

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(FromEmail) &&
        (string.IsNullOrWhiteSpace(Username) || !string.IsNullOrWhiteSpace(Password));
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
            if (!_smtp.IsConfigured)
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
            if (!_smtp.IsConfigured)
            {
                _logger.LogWarning("SMTP not configured, cannot send test email");
                throw new InvalidOperationException("Почта не настроена. Обратитесь к администратору.");
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
        if (!_smtp.IsConfigured)
        {
            _logger.LogWarning("SMTP not configured, email to {Email} not sent", userEmail);
            throw new InvalidOperationException("Почта не настроена. Обратитесь к администратору.");
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

    private Task SendMessageAsync(MimeMessage message) =>
        SmtpEmailSender.SendAsync(_smtp, message);
}

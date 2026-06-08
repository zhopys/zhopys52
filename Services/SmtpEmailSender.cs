using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MiniFinance.Services;

/// <summary>
/// Отправка писем через MailKit с едиными настройками TLS и SMTP-хоста.
/// </summary>
public static class SmtpEmailSender
{
    public static string ResolveHost(SmtpSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Host))
            return settings.Host.Trim();

        var domain = settings.FromEmail.Contains('@', StringComparison.Ordinal)
            ? settings.FromEmail.Split('@')[^1]
            : null;

        return domain?.ToLowerInvariant() switch
        {
            "gmail.com" => "smtp.gmail.com",
            "mtp.by" => "mail.mtp.by",
            _ when !string.IsNullOrEmpty(domain) => $"mail.{domain}",
            _ => "localhost"
        };
    }

    public static async Task SendAsync(SmtpSettings settings, MimeMessage message, CancellationToken cancellationToken = default)
    {
        var host = ResolveHost(settings);
        var port = settings.Port > 0 ? settings.Port : 587;
        var ssl = GetSecureSocketOptions(settings, port);

        using var client = new SmtpClient();
        client.CheckCertificateRevocation = false;
        await client.ConnectAsync(host, port, ssl, cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.Username))
            await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static SecureSocketOptions GetSecureSocketOptions(SmtpSettings settings, int port) =>
        port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            _ => settings.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None
        };
}

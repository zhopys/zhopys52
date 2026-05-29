using System.Globalization;
using System.Net;
using System.Text;

namespace MiniFinance.Services;

public static class EmailTemplateBuilder
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private const string Accent = "#00b894";
    private const string AccentDark = "#008f72";
    private const string AccentBlue = "#2563eb";
    private const string Bg = "#eef4f8";
    private const string Text = "#0b1220";
    private const string TextMuted = "#64748b";
    private const string Danger = "#e11d48";
    private const string Warning = "#d97706";

    public sealed record PaymentReminderModel(
        string PaymentName,
        decimal Amount,
        DateTime DueDate,
        string PaymentType,
        int DaysUntilDue,
        string AppUrl);

    public static (string Subject, string HtmlBody, string TextBody) BuildPaymentReminder(PaymentReminderModel model)
    {
        var typeLabel = model.PaymentType == "tax" ? "Налог" : "Платёж";
        var intro = model.PaymentType == "tax"
            ? "Приближается срок уплаты налога"
            : "Приближается срок оплаты";
        var urgency = GetUrgency(model.DaysUntilDue);
        var targetUrl = BuildTargetUrl(model.AppUrl, model.PaymentType);
        var safeName = WebUtility.HtmlEncode(model.PaymentName);
        var amount = $"{model.Amount.ToString("N2", Ru)} BYN";
        var dueDate = model.DueDate.ToString("dd.MM.yyyy", Ru);
        var weekday = model.DueDate.ToString("dddd", Ru);

        var subject = model.DaysUntilDue switch
        {
            0 => $"MiniFinance: {typeLabel} «{model.PaymentName}» — срок сегодня",
            1 => $"MiniFinance: {typeLabel} «{model.PaymentName}» — завтра",
            _ => $"MiniFinance: {typeLabel} «{model.PaymentName}» — через {model.DaysUntilDue} дн."
        };

        var html = $"""
<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>{WebUtility.HtmlEncode(subject)}</title>
</head>
<body style="margin:0;padding:0;background:{Bg};font-family:'Inter',-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Text};">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Bg};padding:32px 16px;">
    <tr>
      <td align="center">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border-radius:20px;overflow:hidden;border:1px solid rgba(15,23,42,0.08);box-shadow:0 12px 40px rgba(15,23,42,0.08);">
          <tr>
            <td style="padding:28px 28px 24px;background:linear-gradient(135deg,{AccentDark} 0%,{AccentBlue} 100%);">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                <tr>
                  <td>
                    <div style="font-size:13px;font-weight:600;letter-spacing:0.08em;text-transform:uppercase;color:rgba(255,255,255,0.82);">MiniFinance</div>
                    <div style="margin-top:8px;font-size:24px;line-height:1.25;font-weight:700;color:#ffffff;">Напоминание о платеже</div>
                  </td>
                  <td align="right" valign="top">
                    <span style="display:inline-block;padding:8px 12px;border-radius:999px;background:{urgency.BadgeBg};color:{urgency.BadgeColor};font-size:12px;font-weight:700;white-space:nowrap;">{urgency.Label}</span>
                  </td>
                </tr>
              </table>
            </td>
          </tr>
          <tr>
            <td style="padding:28px;">
              <p style="margin:0 0 18px;font-size:15px;line-height:1.6;color:{TextMuted};">
                {WebUtility.HtmlEncode(intro)} — до срока осталось <strong style="color:{AccentDark};">{WebUtility.HtmlEncode(urgency.Label.ToLowerInvariant())}</strong>.
              </p>

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid rgba(15,23,42,0.08);border-radius:16px;overflow:hidden;">
                <tr>
                  <td colspan="2" style="padding:14px 18px;background:rgba(0,184,148,0.08);border-bottom:1px solid rgba(15,23,42,0.06);">
                    <span style="display:inline-block;padding:4px 10px;border-radius:999px;background:#ffffff;color:{AccentDark};font-size:12px;font-weight:700;">{WebUtility.HtmlEncode(typeLabel)}</span>
                  </td>
                </tr>
                <tr>
                  <td style="padding:14px 18px;width:34%;font-size:13px;color:{TextMuted};border-bottom:1px solid rgba(15,23,42,0.06);">Название</td>
                  <td style="padding:14px 18px;font-size:15px;font-weight:700;color:{Text};border-bottom:1px solid rgba(15,23,42,0.06);">{safeName}</td>
                </tr>
                <tr>
                  <td style="padding:14px 18px;font-size:13px;color:{TextMuted};border-bottom:1px solid rgba(15,23,42,0.06);">Сумма</td>
                  <td style="padding:14px 18px;font-size:20px;font-weight:800;color:{Text};border-bottom:1px solid rgba(15,23,42,0.06);">{amount}</td>
                </tr>
                <tr>
                  <td style="padding:14px 18px;font-size:13px;color:{TextMuted};">Срок</td>
                  <td style="padding:14px 18px;font-size:15px;font-weight:700;color:{Text};">
                    {dueDate}
                    <span style="display:block;margin-top:4px;font-size:12px;font-weight:500;color:{TextMuted};">{WebUtility.HtmlEncode(Capitalize(weekday))}</span>
                  </td>
                </tr>
              </table>

              <table role="presentation" cellpadding="0" cellspacing="0" style="margin:24px 0 8px;">
                <tr>
                  <td>
                    <a href="{WebUtility.HtmlEncode(targetUrl)}" style="display:inline-block;padding:14px 28px;border-radius:12px;background:linear-gradient(135deg,{AccentDark} 0%,{AccentBlue} 100%);color:#ffffff;text-decoration:none;font-size:15px;font-weight:700;box-shadow:0 8px 24px rgba(0,143,114,0.25);">
                      Открыть в MiniFinance
                    </a>
                  </td>
                </tr>
              </table>

              <p style="margin:18px 0 0;font-size:12px;line-height:1.6;color:{TextMuted};">
                Уведомления можно отключить в профиле пользователя. Это письмо отправлено автоматически — отвечать на него не нужно.
              </p>
            </td>
          </tr>
          <tr>
            <td style="padding:18px 28px 24px;border-top:1px solid rgba(15,23,42,0.06);background:#f8fafc;text-align:center;">
              <div style="font-size:12px;color:{TextMuted};">MiniFinance · учёт финансов малого бизнеса</div>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";

        var text = new StringBuilder()
            .AppendLine("MiniFinance — напоминание о платеже")
            .AppendLine()
            .AppendLine($"{typeLabel}: {model.PaymentName}")
            .AppendLine($"Сумма: {amount}")
            .AppendLine($"Срок: {dueDate} ({Capitalize(weekday)})")
            .AppendLine($"До оплаты: {urgency.Label}")
            .AppendLine()
            .AppendLine($"Открыть: {targetUrl}")
            .ToString();

        return (subject, html, text);
    }

    public static (string Subject, string HtmlBody, string TextBody) BuildTestEmail(string appUrl)
    {
        var subject = "MiniFinance — тестовое письмо";
        var html = WrapSimpleCard(
            "Почта работает",
            "Тестовое письмо успешно доставлено. Email-уведомления о платежах и налогах настроены корректно.",
            appUrl,
            "Открыть MiniFinance",
            "Проверка SMTP");

        var text = "MiniFinance\n\nТестовое письмо успешно доставлено.\n\n" + appUrl;
        return (subject, html, text);
    }

    public static string WrapIdentityContent(string title, string text, string link, string buttonText) =>
        WrapSimpleCard(title, text, link, buttonText, "MiniFinance", includeLinkFallback: true);

    public static string WrapSimpleCard(
        string title,
        string text,
        string link,
        string buttonText,
        string badge,
        bool includeLinkFallback = false)
    {
        var safeTitle = WebUtility.HtmlEncode(title);
        var safeText = WebUtility.HtmlEncode(text);
        var safeHref = WebUtility.HtmlEncode(link);
        var safeButton = WebUtility.HtmlEncode(buttonText);
        var safeBadge = WebUtility.HtmlEncode(badge);
        var fallback = includeLinkFallback
            ? $"""<p style="margin:18px 0 0;font-size:12px;line-height:1.6;color:{TextMuted};word-break:break-all;">Если кнопка не работает, скопируйте ссылку:<br/>{safeHref}</p>"""
            : string.Empty;

        return $"""
<!DOCTYPE html>
<html lang="ru">
<head><meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1.0" /></head>
<body style="margin:0;padding:0;background:{Bg};font-family:'Inter',-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Text};">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Bg};padding:32px 16px;">
    <tr><td align="center">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;border-radius:20px;overflow:hidden;border:1px solid rgba(15,23,42,0.08);box-shadow:0 12px 40px rgba(15,23,42,0.08);">
        <tr><td style="padding:24px 28px;background:linear-gradient(135deg,{AccentDark} 0%,{AccentBlue} 100%);color:#fff;font-size:22px;font-weight:700;">MiniFinance</td></tr>
        <tr><td style="padding:28px;">
          <span style="display:inline-block;margin-bottom:12px;padding:4px 10px;border-radius:999px;background:rgba(0,184,148,0.12);color:{AccentDark};font-size:12px;font-weight:700;">{safeBadge}</span>
          <h1 style="margin:0 0 12px;font-size:22px;line-height:1.3;color:{Text};">{safeTitle}</h1>
          <p style="margin:0;font-size:15px;line-height:1.6;color:{TextMuted};">{safeText}</p>
          <p style="margin:24px 0 0;">
            <a href="{safeHref}" style="display:inline-block;padding:14px 28px;border-radius:12px;background:linear-gradient(135deg,{AccentDark} 0%,{AccentBlue} 100%);color:#ffffff;text-decoration:none;font-size:15px;font-weight:700;">{safeButton}</a>
          </p>
          {fallback}
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>
""";
    }

    private static (string Label, string BadgeBg, string BadgeColor) GetUrgency(int daysUntilDue) => daysUntilDue switch
    {
        <= 0 => ("Сегодня", "rgba(225,29,72,0.16)", Danger),
        1 => ("Завтра", "rgba(217,119,6,0.16)", Warning),
        <= 3 => ($"Через {daysUntilDue} дн.", "rgba(217,119,6,0.16)", Warning),
        _ => ($"Через {daysUntilDue} дн.", "rgba(0,184,148,0.14)", AccentDark)
    };

    private static string BuildTargetUrl(string appUrl, string paymentType)
    {
        var baseUrl = string.IsNullOrWhiteSpace(appUrl) ? "http://localhost:5210" : appUrl.TrimEnd('/');
        return paymentType == "tax" ? $"{baseUrl}/taxes" : $"{baseUrl}/reminders";
    }

    private static string Capitalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? value : char.ToUpper(value[0]) + value[1..];
}

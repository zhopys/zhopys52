using MiniFinance.Services;
using Xunit;

namespace MiniFinance.Tests;

public class EmailTemplateBuilderTests
{
    [Fact]
    public void BuildPaymentReminder_escapes_html_and_sets_subject_for_today()
    {
        var (subject, html, text) = EmailTemplateBuilder.BuildPaymentReminder(
            new EmailTemplateBuilder.PaymentReminderModel(
                "Аренда <script>",
                1500m,
                new DateTime(2026, 6, 1),
                "reminder",
                0,
                "http://localhost:5210"));

        Assert.Contains("срок сегодня", subject);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("Аренда &lt;script&gt;", html);
        Assert.Contains("500,00 BYN", html.Replace('\u202F', ' '));
        Assert.Contains("http://localhost:5210/reminders", html);
        Assert.Contains("Сегодня", text);
    }

    [Fact]
    public void BuildPaymentReminder_links_taxes_for_tax_type()
    {
        var (_, html, _) = EmailTemplateBuilder.BuildPaymentReminder(
            new EmailTemplateBuilder.PaymentReminderModel(
                "УСН",
                300m,
                DateTime.Today.AddDays(2),
                "tax",
                2,
                "http://localhost:5210"));

        Assert.Contains("/taxes", html);
        Assert.Contains("Налог", html);
    }
}

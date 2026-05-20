namespace MiniFinance.Services;
using MailKit.Security;
public interface INotificationEmailService
{
    Task SendUpcomingPaymentNotificationAsync(string userEmail, string paymentName, decimal amount, DateTime dueDate, string paymentType, int daysUntilDue);
    Task SendTestEmailAsync(string userEmail);
    Task<int> SendAllUpcomingNotificationsAsync(IEnumerable<(string Email, string Name, decimal Amount, DateTime DueDate, string Type, int DaysUntil)> items);
    Task SendRawEmailAsync(string userEmail, string subject, string htmlBody);
}

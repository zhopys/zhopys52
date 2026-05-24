using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class NotificationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<NotificationBackgroundService> _logger;
    private readonly int _checkIntervalHours;

    public NotificationBackgroundService(
        IServiceProvider services,
        ILogger<NotificationBackgroundService> logger,
        IOptions<NotificationSettings> notificationSettings)
    {
        _services = services;
        _logger = logger;
        _checkIntervalHours = notificationSettings.Value.CheckIntervalHours;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification background service started (check interval: {Interval}h)", _checkIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNotificationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing notifications");
            }

            await Task.Delay(TimeSpan.FromHours(_checkIntervalHours), stoppingToken);
        }
    }

    private async Task ProcessNotificationsAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var emailService = scope.ServiceProvider.GetRequiredService<INotificationEmailService>();

        var users = await userManager.Users
            .Where(u => u.EnableNotifications)
            .ToListAsync(ct);

        var now = DateTime.Today;

        foreach (var user in users)
        {
            var upcomingReminders = (await db.Reminders
                .Where(r => r.UserId == user.Id
                    && !r.IsPaid
                    && !r.IsArchived
                    && r.Date >= now
                    && r.NotificationSentDate == null)
                .ToListAsync(ct))
                .Where(r => ReminderScheduleHelper.ShouldNotifyToday(r, now))
                .ToList();

            foreach (var reminder in upcomingReminders)
            {
                var daysUntil = (int)(reminder.Date - now).TotalDays;
                await emailService.SendUpcomingPaymentNotificationAsync(
                    user.Email ?? string.Empty,
                    reminder.Name,
                    reminder.Amount,
                    reminder.Date,
                    "reminder",
                    daysUntil);
                reminder.NotificationSentDate = DateTime.UtcNow;
            }

            var taxCutoff = now.AddDays(user.NotificationDaysBefore);
            var upcomingTaxes = await db.TaxPayments
                .Where(t => t.UserId == user.Id
                    && !t.IsPaid
                    && t.DueDate >= now
                    && t.DueDate <= taxCutoff
                    && t.NotificationSentDate == null)
                .ToListAsync(ct);

            foreach (var tax in upcomingTaxes)
            {
                var daysUntil = (int)(tax.DueDate - now).TotalDays;
                await emailService.SendUpcomingPaymentNotificationAsync(
                    user.Email ?? string.Empty,
                    tax.Name,
                    tax.Amount,
                    tax.DueDate,
                    "tax",
                    daysUntil);
                tax.NotificationSentDate = DateTime.UtcNow;
            }

            if (upcomingReminders.Count + upcomingTaxes.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Sent {Count} notifications for user {UserId}",
                    upcomingReminders.Count + upcomingTaxes.Count, user.Id);
            }
        }
    }
}

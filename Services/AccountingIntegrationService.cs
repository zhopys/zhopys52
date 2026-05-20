using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;

namespace MiniFinance.Services;

public class AccountingIntegrationService : IAccountingIntegrationService
{
    private static readonly ConcurrentDictionary<string, ExportState> _states = new();
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AccountingIntegrationService> _logger;

    public AccountingIntegrationService(ApplicationDbContext db, ILogger<AccountingIntegrationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<AccountingIntegrationDto> GetStatusAsync(string userId)
    {
        var state = _states.GetOrAdd(userId, _ => new ExportState
        {
            Provider = "Моё Дело",
            IsConnected = true
        });

        return Task.FromResult(new AccountingIntegrationDto
        {
            Provider = state.Provider,
            IsConnected = state.IsConnected,
            LastExportAt = state.LastExportAt,
            CanForceExport = state.Status != "exporting",
            Status = state.Status,
            LastError = state.LastError
        });
    }

    public async Task<AccountingIntegrationDto> ForceExportAsync(string userId, DateTime start, DateTime end)
    {
        var state = _states.GetOrAdd(userId, _ => new ExportState { Provider = "Моё Дело", IsConnected = true });
        state.Status = "exporting";
        state.LastError = null;

        try
        {
            var txCount = await _db.Transactions
                .CountAsync(t => t.UserId == userId && t.Date >= start && t.Date <= end);

            await Task.Delay(600);

            if (txCount == 0)
            {
                state.Status = "error";
                state.LastError = "Нет транзакций за выбранный период";
                _logger.LogWarning("Accounting export skipped: no transactions for user {UserId}", userId);
            }
            else
            {
                state.Status = "idle";
                state.LastExportAt = DateTime.UtcNow;
                _logger.LogInformation("Accounting export simulated: {Count} transactions for user {UserId}", txCount, userId);
            }
        }
        catch (Exception ex)
        {
            state.Status = "error";
            state.LastError = "Ошибка выгрузки";
            _logger.LogError(ex, "Accounting export failed for user {UserId}", userId);
        }

        return await GetStatusAsync(userId);
    }

    private sealed class ExportState
    {
        public string Provider { get; set; } = "Моё Дело";
        public bool IsConnected { get; set; }
        public DateTime? LastExportAt { get; set; }
        public string Status { get; set; } = "idle";
        public string? LastError { get; set; }
    }
}

namespace MiniFinance.Services;

public interface IAccountingIntegrationService
{
    Task<AccountingIntegrationDto> GetStatusAsync(string userId);
    Task<AccountingIntegrationDto> ForceExportAsync(string userId, DateTime start, DateTime end);
}

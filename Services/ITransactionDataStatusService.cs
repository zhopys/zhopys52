namespace MiniFinance.Services;

public interface ITransactionDataStatusService
{
    Task<DataStatusDto> GetStatusAsync(string userId, DateTime? periodStart = null, DateTime? periodEnd = null);
}

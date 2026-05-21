namespace MiniFinance.Services;

public interface IAccountProfileService
{
    Task<AccountDataSummaryDto> GetDataSummaryAsync(string userId);
    Task<byte[]> BuildExportJsonAsync(string userId);
    Task<byte[]> BuildExportCsvAsync(string userId);
    Task<(bool Success, string? Error)> DeleteAccountAsync(string userId, string password);
}

public sealed class AccountDataSummaryDto
{
    public int Transactions { get; init; }
    public int Projects { get; init; }
    public int Reminders { get; init; }
    public int TaxPayments { get; init; }
    public int Counterparties { get; init; }
    public int Debts { get; init; }
    public int Tags { get; init; }
    public int Attachments { get; init; }
    public decimal Balance { get; init; }
}

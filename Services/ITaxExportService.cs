namespace MiniFinance.Services;

public interface ITaxExportService
{
    Task<(byte[] Data, string FileName)> BuildTaxPackagePdfAsync(string ownerUserId, DateTime start, DateTime end);
}

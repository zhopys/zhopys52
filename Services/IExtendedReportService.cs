namespace MiniFinance.Services;

public interface IExtendedReportService
{
    Task<ExtendedAnalyticsDto> BuildAsync(string userId, ReportFilters filters, string? counterparty = null);
}

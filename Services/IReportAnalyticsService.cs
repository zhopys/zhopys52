namespace MiniFinance.Services;

public interface IReportAnalyticsService
{
    Task<ReportAnalyticsSnapshot> BuildSnapshotAsync(string userId, ReportFilters filters);
}

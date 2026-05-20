namespace MiniFinance.Services;

/// <summary>Backward-compatible wrapper over <see cref="IReportAnalyticsService"/>.</summary>
public class ReportsDashboardService : IReportsDashboardService
{
    private readonly IReportAnalyticsService _analytics;

    public ReportsDashboardService(IReportAnalyticsService analytics) => _analytics = analytics;

    public async Task<ReportsDashboardDto> GetDashboardAsync(string userId, DateTime start, DateTime end, int forecastHorizonDays = 90)
    {
        var filters = new ReportFilters
        {
            Start = start,
            End = end,
            ForecastDays = forecastHorizonDays
        };
        var snapshot = await _analytics.BuildSnapshotAsync(userId, filters);
        return Map(snapshot);
    }

    private static ReportsDashboardDto Map(ReportAnalyticsSnapshot s) => new()
    {
        DataStatus = s.DataStatus,
        Accounting = s.Accounting,
        CriticalReminders = s.CriticalReminders,
        ExpenseChart = s.ExpenseChart,
        ProfitLoss = s.ProfitLoss,
        CashFlow = s.CashFlow,
        TrialBalance = s.TrialBalance,
        IncomeExpenseBook = s.IncomeExpenseBook,
        MonthlyCashflow = s.MonthlyCashflow,
        Projects = s.Projects,
        Forecast = s.Forecast,
        UncategorizedTransactions = s.UncategorizedTransactions
    };
}

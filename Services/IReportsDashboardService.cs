namespace MiniFinance.Services;

public interface IReportsDashboardService
{
    Task<ReportsDashboardDto> GetDashboardAsync(string userId, DateTime start, DateTime end, int forecastHorizonDays = 90);
    static PeriodRange ResolvePeriod(DateTime? start, DateTime? end)
    {
        var e = end?.Date ?? DateTime.Today;
        var s = start?.Date ?? e.AddMonths(-1);
        if (s > e) (s, e) = (e, s);
        if ((e - s).TotalDays > 1095) s = e.AddYears(-3);
        return new PeriodRange(s, e);
    }
}

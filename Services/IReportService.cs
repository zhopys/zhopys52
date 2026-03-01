using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public interface IReportService
    {
        CategoryReport GetCategoryBreakdown(List<Transaction> transactions);
        List<MonthlyTrend> GetMonthlyTrends(List<Transaction> transactions);
        List<Transaction> GetTopTransactions(List<Transaction> transactions, int count, bool expenses = true);
        List<CashflowEntry> GetCashflow(List<Transaction> transactions);
        List<ProjectSummary> GetProjectReport(List<Transaction> transactions);
        List<ForecastPoint> GetForecast(List<MonthlyTrend> monthlyTrends, int monthsAhead = 6);
    }
}

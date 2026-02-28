using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public interface IReportService
    {
        CategoryReport GetCategoryBreakdown(List<Transaction> transactions);
        List<MonthlyTrend> GetMonthlyTrends(List<Transaction> transactions);
        List<Transaction> GetTopTransactions(List<Transaction> transactions, int count, bool expenses = true);
    }
}

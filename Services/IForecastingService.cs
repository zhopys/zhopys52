using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public interface IForecastingService
    {
        ForecastResult PredictNextMonth(List<Transaction> transactions);
        List<CategoryForecast> PredictByCategory(List<Transaction> transactions);
        List<CashForecastPoint> PredictCashflowNextDays(List<Transaction> transactions, List<MiniFinance.Data.Models.Reminder> reminders, int days = 30);
    }
}

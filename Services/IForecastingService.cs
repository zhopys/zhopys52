using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public interface IForecastingService
    {
        ForecastResult PredictNextMonth(List<Transaction> transactions);
        List<CategoryForecast> PredictByCategory(List<Transaction> transactions);
        List<CashForecastPoint> PredictCashflowNextDays(List<Transaction> transactions, List<Reminder> reminders, int days = 30);
        List<CashGap> DetectCashGaps(List<Transaction> transactions, List<Reminder> reminders, int days = 90, decimal threshold = 0);
        AdvancedCashForecast ForecastCashGapsAdvanced(List<Transaction> transactions, List<Reminder> reminders, List<TaxPayment> taxPayments, int days = 90);
        AdvancedCashForecast ForecastCashGapsAdvanced(
            List<Transaction> transactions,
            List<Reminder> reminders,
            List<TaxPayment> taxPayments,
            int days,
            decimal expenseAdjustPercent,
            int incomeDelayDays,
            decimal minCashThreshold);
    }
}

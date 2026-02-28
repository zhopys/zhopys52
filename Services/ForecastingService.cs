using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public class ForecastingService : IForecastingService
    {
        public ForecastResult PredictNextMonth(List<Transaction> transactions)
        {
            if (!transactions.Any())
            {
                return new ForecastResult
                {
                    PredictedIncome = 0,
                    PredictedExpense = 0,
                    PredictedBalance = 0,
                    Confidence = 0
                };
            }

            // Берем данные за последние 3 месяца для прогноза
            var threeMonthsAgo = DateTime.Today.AddMonths(-3);
            var recentTransactions = transactions
                .Where(t => t.Date >= threeMonthsAgo)
                .ToList();

            if (!recentTransactions.Any())
            {
                recentTransactions = transactions;
            }

            // Группируем по месяцам
            var monthlyData = recentTransactions
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .Select(g => new
                {
                    Income = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                    Expense = g.Where(t => t.Amount < 0).Sum(t => t.Amount)
                })
                .ToList();

            // Рассчитываем средние значения
            var avgIncome = monthlyData.Any() ? monthlyData.Average(m => m.Income) : 0;
            var avgExpense = monthlyData.Any() ? monthlyData.Average(m => m.Expense) : 0;

            // Уровень уверенности зависит от количества данных
            var confidence = Math.Min(monthlyData.Count * 25, 100);

            return new ForecastResult
            {
                PredictedIncome = avgIncome,
                PredictedExpense = avgExpense,
                PredictedBalance = avgIncome + avgExpense, // expense уже отрицательный
                Confidence = confidence,
                BasedOnMonths = monthlyData.Count
            };
        }

        public List<CategoryForecast> PredictByCategory(List<Transaction> transactions)
        {
            var threeMonthsAgo = DateTime.Today.AddMonths(-3);
            var recentTransactions = transactions
                .Where(t => t.Date >= threeMonthsAgo)
                .ToList();

            if (!recentTransactions.Any())
            {
                return new List<CategoryForecast>();
            }

            // Группируем по категориям
            var categoryData = recentTransactions
                .GroupBy(t => t.Category)
                .Select(g =>
                {
                    var monthlyAvg = g
                        .GroupBy(t => new { t.Date.Year, t.Date.Month })
                        .Average(m => m.Sum(t => t.Amount));

                    return new CategoryForecast
                    {
                        Category = g.Key,
                        PredictedAmount = monthlyAvg,
                        TransactionCount = g.Count(),
                        IsExpense = monthlyAvg < 0
                    };
                })
                .OrderBy(c => c.PredictedAmount)
                .ToList();

            return categoryData;
        }
    }

    public class ForecastResult
    {
        public decimal PredictedIncome { get; set; }
        public decimal PredictedExpense { get; set; }
        public decimal PredictedBalance { get; set; }
        public int Confidence { get; set; }
        public int BasedOnMonths { get; set; }
    }

    public class CategoryForecast
    {
        public string Category { get; set; } = string.Empty;
        public decimal PredictedAmount { get; set; }
        public int TransactionCount { get; set; }
        public bool IsExpense { get; set; }
    }
}

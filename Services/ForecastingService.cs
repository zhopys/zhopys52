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

        // Predict daily cash balance for next `days` days.
        // Algorithm:
        // - current balance = all transactions up to today (income + expense)
        // - subtract unpaid reminders scheduled within next `days`
        // - compute average daily income from past 3 months and add to each day
        // - produce daily balances
        public List<CashForecastPoint> PredictCashflowNextDays(List<Transaction> transactions, List<MiniFinance.Data.Models.Reminder> reminders, int days = 30)
        {
            var result = new List<CashForecastPoint>();

            // current balance: consider all transactions up to today
            var today = DateTime.Today;
            decimal currentBalance = transactions.Where(t => t.Date <= today).Sum(t => t.Amount);

            // Upcoming reminder outflows (sum per day)
            var upcoming = reminders?.Where(r => !r.IsPaid && r.Date >= today && r.Date <= today.AddDays(days)).ToList() ?? new List<MiniFinance.Data.Models.Reminder>();

            // Average daily income over past 3 months
            var threeMonthsAgo = today.AddMonths(-3);
            var recent = transactions.Where(t => t.Date >= threeMonthsAgo && t.Date <= today).ToList();
            decimal totalRecentIncome = recent.Where(t => t.Amount > 0).Sum(t => t.Amount);
            int daysSpan = (today - threeMonthsAgo).Days;
            if (daysSpan <= 0) daysSpan = 1;
            decimal avgDailyIncome = totalRecentIncome / daysSpan;

            // Build a lookup for reminders by date
            var reminderLookup = upcoming.GroupBy(r => r.Date.Date).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

            decimal bal = currentBalance;
            for (int i = 1; i <= days; i++)
            {
                var d = today.AddDays(i);

                // start from previous day balance
                // add average income for the day
                bal += avgDailyIncome;

                // subtract reminders for the day
                if (reminderLookup.TryGetValue(d.Date, out var remSum))
                {
                    bal -= remSum;
                }

                result.Add(new CashForecastPoint { Date = d.Date, Balance = bal });
            }

            return result;
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

    public class CashForecastPoint
    {
        public DateTime Date { get; set; }
        public decimal Balance { get; set; }
    }

    public class CategoryForecast
    {
        public string Category { get; set; } = string.Empty;
        public decimal PredictedAmount { get; set; }
        public int TransactionCount { get; set; }
        public bool IsExpense { get; set; }
    }
}

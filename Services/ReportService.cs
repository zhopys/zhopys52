using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public class ReportService : IReportService
    {
        public CategoryReport GetCategoryBreakdown(List<Transaction> transactions)
        {
            var report = new CategoryReport
            {
                TotalIncome = transactions.Where(t => t.Amount > 0).Sum(t => t.Amount),
                TotalExpense = transactions.Where(t => t.Amount < 0).Sum(t => t.Amount),
                Balance = transactions.Sum(t => t.Amount)
            };

            // Группировка по категориям
            report.IncomeByCategory = transactions
                .Where(t => t.Amount > 0)
                .GroupBy(t => t.Category)
                .Select(g => new CategorySummary
                {
                    Category = g.Key,
                    Amount = g.Sum(t => t.Amount),
                    Count = g.Count(),
                    Percentage = 0 // Рассчитаем позже
                })
                .OrderByDescending(c => c.Amount)
                .ToList();

            report.ExpenseByCategory = transactions
                .Where(t => t.Amount < 0)
                .GroupBy(t => t.Category)
                .Select(g => new CategorySummary
                {
                    Category = g.Key,
                    Amount = Math.Abs(g.Sum(t => t.Amount)),
                    Count = g.Count(),
                    Percentage = 0
                })
                .OrderByDescending(c => c.Amount)
                .ToList();

            // Рассчитываем проценты
            foreach (var item in report.IncomeByCategory)
            {
                item.Percentage = report.TotalIncome > 0
                    ? (item.Amount / report.TotalIncome) * 100
                    : 0;
            }

            foreach (var item in report.ExpenseByCategory)
            {
                var totalExpenseAbs = Math.Abs(report.TotalExpense);
                item.Percentage = totalExpenseAbs > 0
                    ? (item.Amount / totalExpenseAbs) * 100
                    : 0;
            }

            return report;
        }

        public List<MonthlyTrend> GetMonthlyTrends(List<Transaction> transactions)
        {
            return transactions
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .Select(g => new MonthlyTrend
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Income = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                    Expense = Math.Abs(g.Where(t => t.Amount < 0).Sum(t => t.Amount)),
                    Balance = g.Sum(t => t.Amount),
                    TransactionCount = g.Count()
                })
                .OrderBy(m => m.Year)
                .ThenBy(m => m.Month)
                .ToList();
        }

        public List<Transaction> GetTopTransactions(List<Transaction> transactions, int count, bool expenses = true)
        {
            return expenses
                ? transactions.Where(t => t.Amount < 0).OrderBy(t => t.Amount).Take(count).ToList()
                : transactions.Where(t => t.Amount > 0).OrderByDescending(t => t.Amount).Take(count).ToList();
        }

        public List<CashflowEntry> GetCashflow(List<Transaction> transactions)
        {
            var grouped = transactions
                .GroupBy(t => t.Date.Date)
                .OrderBy(g => g.Key)
                .Select(g => new CashflowEntry
                {
                    Date = g.Key,
                    Income = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                    Expense = Math.Abs(g.Where(t => t.Amount < 0).Sum(t => t.Amount)),
                    Balance = g.Sum(t => t.Amount)
                })
                .ToList();

            // cumulative balance
            decimal running = 0;
            foreach (var item in grouped)
            {
                running += item.Balance;
                item.Balance = running;
            }

            return grouped;
        }

        public List<ProjectSummary> GetProjectReport(List<Transaction> transactions)
        {
            // Group by project name if available, otherwise fallback to Category
            var groups = transactions
                .GroupBy(t => t.Project != null ? t.Project.Name : t.Category)
                .Select(g => new ProjectSummary
                {
                    Project = string.IsNullOrWhiteSpace(g.Key) ? "(none)" : g.Key,
                    Income = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                    Expense = Math.Abs(g.Where(t => t.Amount < 0).Sum(t => t.Amount)),
                    Transactions = g.Count()
                })
                .OrderByDescending(p => p.Profit)
                .ToList();

            return groups;
        }

        // Simple forecast based on linear regression over monthly balances.
        // If there are fewer than 2 points, we fall back to repeating the last known balance.
        public List<ForecastPoint> GetForecast(List<MonthlyTrend> monthlyTrends, int monthsAhead = 6)
        {
            var result = new List<ForecastPoint>();

            if (monthlyTrends == null || monthlyTrends.Count == 0 || monthsAhead <= 0)
                return result;

            // Use monthly balance as the target value. Ensure sorted by time.
            var ordered = monthlyTrends.OrderBy(m => new DateTime(m.Year, m.Month, 1)).ToList();

            // Convert to numeric x,y where x is consecutive month index starting at 0
            var values = ordered.Select(m => m.Balance).ToList();
            int n = values.Count;

            if (n == 1)
            {
                // Not enough data to project; repeat the single value
                var start = new DateTime(ordered[0].Year, ordered[0].Month, 1);
                for (int i = 1; i <= monthsAhead; i++)
                {
                    var d = start.AddMonths(i);
                    result.Add(new ForecastPoint { Year = d.Year, Month = d.Month, Balance = ordered[0].Balance });
                }
                return result;
            }

            // Compute linear regression (least squares) for y = a + b*x
            // x: 0 .. n-1
            double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
            for (int i = 0; i < n; i++)
            {
                double x = i;
                double y = (double)values[i];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
            }

            double denom = n * sumXX - sumX * sumX;
            double slope = denom == 0 ? 0 : (n * sumXY - sumX * sumY) / denom;
            double intercept = (sumY - slope * sumX) / n;

            var lastDate = new DateTime(ordered.Last().Year, ordered.Last().Month, 1);
            for (int i = 1; i <= monthsAhead; i++)
            {
                int xForecast = n - 1 + i; // next indices
                double yPred = intercept + slope * xForecast;
                var d = lastDate.AddMonths(i);
                result.Add(new ForecastPoint
                {
                    Year = d.Year,
                    Month = d.Month,
                    Balance = (decimal)Math.Round(yPred, 2)
                });
            }

            return result;
        }

    }

    public class CategoryReport
    {
        public decimal TotalIncome { get; set; }
        public decimal TotalExpense { get; set; }
        public decimal Balance { get; set; }
        public List<CategorySummary> IncomeByCategory { get; set; } = new();
        public List<CategorySummary> ExpenseByCategory { get; set; } = new();
    }

    public class CategorySummary
    {
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public int Count { get; set; }
        public decimal Percentage { get; set; }
    }

    public class MonthlyTrend
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public decimal Balance { get; set; }
        public int TransactionCount { get; set; }

        public string MonthName => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    }

    public class CashflowEntry
    {
        public DateTime Date { get; set; }
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public decimal Balance { get; set; }
    }

    public class ProjectSummary
    {
        public string Project { get; set; } = string.Empty;
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public decimal Profit => Income - Expense;
        public int Transactions { get; set; }
    }

    // Forecast point used for simple projection
    public class ForecastPoint
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal Balance { get; set; }
        public string MonthName => new DateTime(Year, Month, 1).ToString("MMM yyyy");
    }
}

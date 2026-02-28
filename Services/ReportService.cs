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
            // Try to use a 'Project' property if it exists on Transaction, otherwise fallback to Category
            var projProp = typeof(Transaction).GetProperty("Project");

            var groups = transactions
                .GroupBy(t => projProp != null ? (projProp.GetValue(t)?.ToString() ?? "(none)") : t.Category)
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
}

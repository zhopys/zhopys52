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

        // Отчет о прибылях и убытках (P&L)
        public ProfitLossReport GetProfitLossReport(List<Transaction> transactions, DateTime startDate, DateTime endDate)
        {
            var filtered = transactions.Where(t => t.Date >= startDate && t.Date <= endDate).ToList();

            var report = new ProfitLossReport
            {
                StartDate = startDate,
                EndDate = endDate,
                TotalIncome = filtered.Where(t => t.Amount > 0).Sum(t => t.Amount),
                TotalExpense = Math.Abs(filtered.Where(t => t.Amount < 0).Sum(t => t.Amount))
            };

            report.IncomeByCategory = filtered
                .Where(t => t.Amount > 0)
                .GroupBy(t => t.Category)
                .Select(g => new PLCategoryItem
                {
                    Category = g.Key,
                    Amount = g.Sum(t => t.Amount),
                    Percentage = 0
                })
                .OrderByDescending(c => c.Amount)
                .ToList();

            report.ExpenseByCategory = filtered
                .Where(t => t.Amount < 0)
                .GroupBy(t => t.Category)
                .Select(g => new PLCategoryItem
                {
                    Category = g.Key,
                    Amount = Math.Abs(g.Sum(t => t.Amount)),
                    Percentage = 0
                })
                .OrderByDescending(c => c.Amount)
                .ToList();

            // Рассчитываем проценты
            foreach (var item in report.IncomeByCategory)
            {
                item.Percentage = report.TotalIncome > 0 ? (item.Amount / report.TotalIncome) * 100 : 0;
            }

            foreach (var item in report.ExpenseByCategory)
            {
                item.Percentage = report.TotalExpense > 0 ? (item.Amount / report.TotalExpense) * 100 : 0;
            }

            report.NetProfit = report.TotalIncome - report.TotalExpense;
            report.ProfitMargin = report.TotalIncome > 0 ? (report.NetProfit / report.TotalIncome) * 100 : 0;

            return report;
        }

        // Оборотно-сальдовая ведомость
        public TrialBalanceReport GetTrialBalanceReport(List<Transaction> transactions, DateTime startDate, DateTime endDate)
        {
            var beforePeriod = transactions.Where(t => t.Date < startDate).ToList();
            var inPeriod = transactions.Where(t => t.Date >= startDate && t.Date <= endDate).ToList();
            var allUpToEnd = transactions.Where(t => t.Date <= endDate).ToList();

            var categories = transactions.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();

            var report = new TrialBalanceReport
            {
                StartDate = startDate,
                EndDate = endDate,
                Entries = new List<TrialBalanceEntry>()
            };

            foreach (var category in categories)
            {
                var openingBalance = beforePeriod.Where(t => t.Category == category).Sum(t => t.Amount);
                var debit = inPeriod.Where(t => t.Category == category && t.Amount > 0).Sum(t => t.Amount);
                var credit = Math.Abs(inPeriod.Where(t => t.Category == category && t.Amount < 0).Sum(t => t.Amount));
                var closingBalance = allUpToEnd.Where(t => t.Category == category).Sum(t => t.Amount);

                report.Entries.Add(new TrialBalanceEntry
                {
                    Category = category,
                    OpeningBalance = openingBalance,
                    Debit = debit,
                    Credit = credit,
                    ClosingBalance = closingBalance
                });
            }

            report.TotalOpeningBalance = report.Entries.Sum(e => e.OpeningBalance);
            report.TotalDebit = report.Entries.Sum(e => e.Debit);
            report.TotalCredit = report.Entries.Sum(e => e.Credit);
            report.TotalClosingBalance = report.Entries.Sum(e => e.ClosingBalance);

            return report;
        }

        // Книга доходов и расходов (УСН)
        public List<IncomeExpenseBookEntry> GetIncomeExpenseBook(List<Transaction> transactions, DateTime startDate, DateTime endDate)
        {
            var filtered = transactions
                .Where(t => t.Date >= startDate && t.Date <= endDate)
                .OrderBy(t => t.Date)
                .ToList();

            var entries = new List<IncomeExpenseBookEntry>();
            int entryNumber = 1;

            foreach (var transaction in filtered)
            {
                var idString = transaction.Id.ToString();
                entries.Add(new IncomeExpenseBookEntry
                {
                    EntryNumber = entryNumber++,
                    Date = transaction.Date,
                    DocumentNumber = idString.Length >= 8 ? idString.Substring(0, 8) : idString.PadLeft(8, '0'),
                    Counterparty = transaction.Counterparty ?? "Не указан",
                    Description = transaction.Description ?? transaction.Category,
                    Income = transaction.Amount > 0 ? transaction.Amount : 0,
                    Expense = transaction.Amount < 0 ? Math.Abs(transaction.Amount) : 0,
                    Category = transaction.Category
                });
            }

            return entries;
        }

        // Отчет о движении денежных средств (ДДС)
        public CashFlowStatementReport GetCashFlowStatement(List<Transaction> transactions, DateTime startDate, DateTime endDate)
        {
            var filtered = transactions.Where(t => t.Date >= startDate && t.Date <= endDate).ToList();

            var report = new CashFlowStatementReport
            {
                StartDate = startDate,
                EndDate = endDate,
                TransactionCount = filtered.Count
            };

            foreach (var t in filtered)
            {
                var activity = CashFlowActivityHelper.Classify(t);
                if (t.Amount > 0)
                    AddIncome(report, activity, t.Amount);
                else
                    AddExpense(report, activity, Math.Abs(t.Amount));
            }

            report.OperatingCashFlow = report.OperatingIncome - report.OperatingExpenses;
            report.InvestmentCashFlow = report.InvestmentIncome - report.InvestmentExpenses;
            report.FinancingCashFlow = report.FinancingIncome - report.FinancingExpenses;
            report.NetCashFlow = report.OperatingCashFlow + report.InvestmentCashFlow + report.FinancingCashFlow;

            report.CategoryDetails = filtered
                .GroupBy(t => (CashFlowActivityHelper.Classify(t), Category: string.IsNullOrWhiteSpace(t.Category) ? "Без категории" : t.Category))
                .Select(g => new CashFlowCategoryDetail
                {
                    Activity = g.Key.Item1,
                    Category = g.Key.Category,
                    Income = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                    Expense = Math.Abs(g.Where(t => t.Amount < 0).Sum(t => t.Amount)),
                    NetFlow = g.Sum(t => t.Amount),
                    TransactionCount = g.Count()
                })
                .OrderBy(c => c.Activity)
                .ThenByDescending(c => Math.Abs(c.NetFlow))
                .ToList();

            report.ActivityDetails = Enum.GetValues<CashFlowActivity>()
                .Cast<CashFlowActivity>()
                .Select(BuildActivityDetail)
                .ToList();

            return report;

            CashFlowActivityDetail BuildActivityDetail(CashFlowActivity activity)
            {
                var rows = report.CategoryDetails.Where(c => c.Activity == activity).ToList();
                return activity switch
                {
                    CashFlowActivity.Investment => new CashFlowActivityDetail
                    {
                        Activity = activity,
                        Income = report.InvestmentIncome,
                        Expense = report.InvestmentExpenses,
                        NetFlow = report.InvestmentCashFlow,
                        TransactionCount = rows.Sum(r => r.TransactionCount),
                        Categories = rows
                    },
                    CashFlowActivity.Financing => new CashFlowActivityDetail
                    {
                        Activity = activity,
                        Income = report.FinancingIncome,
                        Expense = report.FinancingExpenses,
                        NetFlow = report.FinancingCashFlow,
                        TransactionCount = rows.Sum(r => r.TransactionCount),
                        Categories = rows
                    },
                    _ => new CashFlowActivityDetail
                    {
                        Activity = activity,
                        Income = report.OperatingIncome,
                        Expense = report.OperatingExpenses,
                        NetFlow = report.OperatingCashFlow,
                        TransactionCount = rows.Sum(r => r.TransactionCount),
                        Categories = rows
                    }
                };
            }

            static void AddIncome(CashFlowStatementReport r, CashFlowActivity activity, decimal amount)
            {
                switch (activity)
                {
                    case CashFlowActivity.Investment: r.InvestmentIncome += amount; break;
                    case CashFlowActivity.Financing: r.FinancingIncome += amount; break;
                    default: r.OperatingIncome += amount; break;
                }
            }

            static void AddExpense(CashFlowStatementReport r, CashFlowActivity activity, decimal amount)
            {
                switch (activity)
                {
                    case CashFlowActivity.Investment: r.InvestmentExpenses += amount; break;
                    case CashFlowActivity.Financing: r.FinancingExpenses += amount; break;
                    default: r.OperatingExpenses += amount; break;
                }
            }
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

    // Отчет о прибылях и убытках
    public class ProfitLossReport
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal TotalIncome { get; set; }
        public decimal TotalExpense { get; set; }
        public decimal NetProfit { get; set; }
        public decimal ProfitMargin { get; set; }
        public List<PLCategoryItem> IncomeByCategory { get; set; } = new();
        public List<PLCategoryItem> ExpenseByCategory { get; set; } = new();
    }

    public class PLCategoryItem
    {
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal Percentage { get; set; }
    }

    // Оборотно-сальдовая ведомость
    public class TrialBalanceReport
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<TrialBalanceEntry> Entries { get; set; } = new();
        public decimal TotalOpeningBalance { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal TotalClosingBalance { get; set; }
    }

    public class TrialBalanceEntry
    {
        public string Category { get; set; } = string.Empty;
        public decimal OpeningBalance { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal ClosingBalance { get; set; }
    }

    // Книга доходов и расходов
    public class IncomeExpenseBookEntry
    {
        public int EntryNumber { get; set; }
        public DateTime Date { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public string Counterparty { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    // Отчет о движении денежных средств
    public class CashFlowStatementReport
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // Операционная деятельность
        public decimal OperatingIncome { get; set; }
        public decimal OperatingExpenses { get; set; }
        public decimal OperatingCashFlow { get; set; }

        // Инвестиционная деятельность
        public decimal InvestmentIncome { get; set; }
        public decimal InvestmentExpenses { get; set; }
        public decimal InvestmentCashFlow { get; set; }

        // Финансовая деятельность
        public decimal FinancingIncome { get; set; }
        public decimal FinancingExpenses { get; set; }
        public decimal FinancingCashFlow { get; set; }

        // Итого
        public decimal NetCashFlow { get; set; }
        public int TransactionCount { get; set; }

        public List<CashFlowActivityDetail> ActivityDetails { get; set; } = new();
        public List<CashFlowCategoryDetail> CategoryDetails { get; set; } = new();
    }

    public class CashFlowActivityDetail
    {
        public CashFlowActivity Activity { get; set; }
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public decimal NetFlow { get; set; }
        public int TransactionCount { get; set; }
        public List<CashFlowCategoryDetail> Categories { get; set; } = new();
    }

    public class CashFlowCategoryDetail
    {
        public CashFlowActivity Activity { get; set; }
        public string Category { get; set; } = string.Empty;
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public decimal NetFlow { get; set; }
        public int TransactionCount { get; set; }
    }
}

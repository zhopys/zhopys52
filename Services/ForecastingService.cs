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

            var threeMonthsAgo = DateTime.Today.AddMonths(-3);
            var recentTransactions = transactions
                .Where(t => t.Date >= threeMonthsAgo)
                .ToList();

            if (!recentTransactions.Any())
            {
                recentTransactions = transactions;
            }

            var monthlyData = recentTransactions
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .Select(g => new
                {
                    Income = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                    Expense = g.Where(t => t.Amount < 0).Sum(t => t.Amount)
                })
                .ToList();

            var avgIncome = monthlyData.Any() ? monthlyData.Average(m => m.Income) : 0;
            var avgExpense = monthlyData.Any() ? monthlyData.Average(m => m.Expense) : 0;

            var confidence = Math.Min(monthlyData.Count * 25, 100);

            return new ForecastResult
            {
                PredictedIncome = avgIncome,
                PredictedExpense = avgExpense,
                PredictedBalance = avgIncome + avgExpense,
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

        public List<CashForecastPoint> PredictCashflowNextDays(List<Transaction> transactions, List<Reminder> reminders, int days = 30)
        {
            var result = new List<CashForecastPoint>();

            var today = DateTime.Today;
            decimal currentBalance = transactions.Where(t => t.Date <= today).Sum(t => t.Amount);

            var upcoming = reminders?.Where(r => !r.IsPaid && r.Date >= today && r.Date <= today.AddDays(days)).ToList() ?? new List<Reminder>();

            var threeMonthsAgo = today.AddMonths(-3);
            var recent = transactions.Where(t => t.Date >= threeMonthsAgo && t.Date <= today).ToList();
            decimal totalRecentIncome = recent.Where(t => t.Amount > 0).Sum(t => t.Amount);
            int daysSpan = (today - threeMonthsAgo).Days;
            if (daysSpan <= 0) daysSpan = 1;
            decimal avgDailyIncome = totalRecentIncome / daysSpan;

            var reminderLookup = upcoming.GroupBy(r => r.Date.Date).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

            decimal bal = currentBalance;
            for (int i = 1; i <= days; i++)
            {
                var d = today.AddDays(i);
                bal += avgDailyIncome;

                if (reminderLookup.TryGetValue(d.Date, out var remSum))
                {
                    bal -= remSum;
                }

                result.Add(new CashForecastPoint { Date = d.Date, Balance = bal });
            }

            return result;
        }

        public List<CashGap> DetectCashGaps(List<Transaction> transactions, List<Reminder> reminders, int days = 90, decimal threshold = 0)
        {
            var forecast = PredictCashflowNextDays(transactions, reminders, days);
            var gaps = new List<CashGap>();

            CashGap? currentGap = null;

            foreach (var point in forecast)
            {
                if (point.Balance < threshold)
                {
                    if (currentGap == null)
                    {
                        currentGap = new CashGap
                        {
                            StartDate = point.Date,
                            MinBalance = point.Balance
                        };
                    }
                    else
                    {
                        currentGap.EndDate = point.Date;
                        if (point.Balance < currentGap.MinBalance)
                        {
                            currentGap.MinBalance = point.Balance;
                        }
                    }
                }
                else
                {
                    if (currentGap != null)
                    {
                        currentGap.EndDate = currentGap.EndDate ?? currentGap.StartDate;
                        currentGap.Duration = (currentGap.EndDate.Value - currentGap.StartDate).Days + 1;
                        currentGap.Severity = CalculateGapSeverity(currentGap.MinBalance, threshold);
                        gaps.Add(currentGap);
                        currentGap = null;
                    }
                }
            }

            if (currentGap != null)
            {
                currentGap.EndDate = forecast.Last().Date;
                currentGap.Duration = (currentGap.EndDate.Value - currentGap.StartDate).Days + 1;
                currentGap.Severity = CalculateGapSeverity(currentGap.MinBalance, threshold);
                gaps.Add(currentGap);
            }

            return gaps;
        }

        private GapSeverity CalculateGapSeverity(decimal minBalance, decimal threshold)
        {
            var deficit = threshold - minBalance;

            if (deficit <= 0) return GapSeverity.None;
            if (deficit < 10000) return GapSeverity.Low;
            if (deficit < 50000) return GapSeverity.Medium;
            if (deficit < 100000) return GapSeverity.High;
            return GapSeverity.Critical;
        }

        public AdvancedCashForecast ForecastCashGapsAdvanced(
            List<Transaction> transactions,
            List<Reminder> reminders,
            List<TaxPayment> taxPayments,
            int days = 90)
        {
            var today = DateTime.Today;
            var endDate = today.AddDays(days);

            // Current balance
            var currentBalance = transactions.Where(t => t.Date <= today).Sum(t => t.Amount);

            // Monthly income/expense for seasonal analysis
            var monthlyGroups = transactions
                .Where(t => t.Date < today)
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .OrderBy(g => g.Key.Year)
                .ThenBy(g => g.Key.Month)
                .Select(g => (
                    Date: new DateTime(g.Key.Year, g.Key.Month, 1),
                    Income: g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                    Expense: Math.Abs(g.Where(t => t.Amount < 0).Sum(t => t.Amount)),
                    Net: g.Sum(t => t.Amount)
                ))
                .ToList();

            // Seasonal adjustment: compute month-of-month averages
            var seasonalIncome = new Dictionary<int, decimal>();
            var seasonalExpense = new Dictionary<int, decimal>();
            for (int m = 1; m <= 12; m++)
            {
                var monthData = monthlyGroups.Where(g => g.Date.Month == m).ToList();
                seasonalIncome[m] = monthData.Any() ? monthData.Average(g => g.Income) : 0;
                seasonalExpense[m] = monthData.Any() ? monthData.Average(g => g.Expense) : 0;
            }

            // Trend via linear regression on last 6 months
            var last6 = monthlyGroups.TakeLast(6).ToList();
            var (incomeTrend, expenseTrend) = ComputeTrends(last6);

            // Build scenarios
            var baseScenario = BuildScenario(transactions, reminders, taxPayments, today, days,
                currentBalance, seasonalIncome, seasonalExpense, incomeTrend, expenseTrend, 1.0m);

            var optimisticScenario = BuildScenario(transactions, reminders, taxPayments, today, days,
                currentBalance, seasonalIncome, seasonalExpense, incomeTrend, expenseTrend, 1.2m);

            var pessimisticScenario = BuildScenario(transactions, reminders, taxPayments, today, days,
                currentBalance, seasonalIncome, seasonalExpense, incomeTrend, expenseTrend, 0.7m);

            // Detect gaps in base scenario
            var gaps = DetectGapsInForecast(baseScenario.Points, 0);

            // Key dates: reminders + tax payments
            var keyDates = new List<KeyDate>();
            foreach (var r in reminders.Where(r => !r.IsPaid && r.Date >= today && r.Date <= endDate).OrderBy(r => r.Date))
            {
                keyDates.Add(new KeyDate
                {
                    Date = r.Date,
                    Label = r.Name,
                    Amount = -r.Amount,
                    Type = "reminder"
                });
            }
            foreach (var t in taxPayments.Where(t => !t.IsPaid && t.DueDate >= today && t.DueDate <= endDate).OrderBy(t => t.DueDate))
            {
                keyDates.Add(new KeyDate
                {
                    Date = t.DueDate,
                    Label = t.Name,
                    Amount = -t.Amount,
                    Type = "tax"
                });
            }

            // Metrics
            var minBalance = baseScenario.Points.Any() ? baseScenario.Points.Min(p => p.Balance) : 0;
            var maxBalance = baseScenario.Points.Any() ? baseScenario.Points.Max(p => p.Balance) : 0;
            var avgDailyIncome = baseScenario.Points.Any() ? baseScenario.Points.Average(p => p.Income) : 0;
            var avgDailyExpense = baseScenario.Points.Any() ? baseScenario.Points.Average(p => p.Expense) : 0;
            var daysInRed = baseScenario.Points.Count(p => p.Balance < 0);
            var recoveryDays = gaps.Any() ? gaps.Min(g => (g.EndDate ?? g.StartDate) - g.StartDate).Days + 1 : 0;

            return new AdvancedCashForecast
            {
                CurrentBalance = currentBalance,
                BaseScenario = baseScenario,
                OptimisticScenario = optimisticScenario,
                PessimisticScenario = pessimisticScenario,
                Gaps = gaps,
                KeyDates = keyDates,
                MinBalance = minBalance,
                MaxBalance = maxBalance,
                AvgDailyIncome = avgDailyIncome,
                AvgDailyExpense = avgDailyExpense,
                DaysInRed = daysInRed,
                RecoveryDays = recoveryDays,
                HasRisk = minBalance < 0,
                RiskLevel = minBalance < -100000 ? "critical" : minBalance < -50000 ? "high" : minBalance < -10000 ? "medium" : minBalance < 0 ? "low" : "safe"
            };
        }

        private (decimal incomeTrend, decimal expenseTrend) ComputeTrends(List<(DateTime Date, decimal Income, decimal Expense, decimal Net)> monthlyData)
        {
            if (monthlyData.Count < 2) return (0, 0);

            var n = monthlyData.Count;
            double sumX = 0, sumYi = 0, sumXi = 0, sumXX = 0, sumXe = 0;
            for (int i = 0; i < n; i++)
            {
                sumX += i;
                sumYi += (double)monthlyData[i].Income;
                sumXi += i * (double)monthlyData[i].Income;
                sumXX += i * i;
                sumXe += i * (double)monthlyData[i].Expense;
            }

            var denom = n * sumXX - sumX * sumX;
            if (denom == 0) return (0, 0);

            var incomeSlope = (n * sumXi - sumX * sumYi) / denom;
            var expenseSlope = (n * sumXe - sumX * (monthlyData.Sum(m => (double)m.Expense))) / denom;

            return ((decimal)incomeSlope, (decimal)expenseSlope);
        }

        private CashScenario BuildScenario(
            List<Transaction> transactions,
            List<Reminder> reminders,
            List<TaxPayment> taxPayments,
            DateTime today,
            int days,
            decimal currentBalance,
            Dictionary<int, decimal> seasonalIncome,
            Dictionary<int, decimal> seasonalExpense,
            decimal incomeTrend,
            decimal expenseTrend,
            decimal multiplier)
        {
            var points = new List<ScenarioPoint>();
            var endDate = today.AddDays(days);

            // Upcoming fixed outflows
            var reminderByDate = reminders
                .Where(r => !r.IsPaid && r.Date >= today && r.Date <= endDate)
                .GroupBy(r => r.Date.Date)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

            var taxByDate = taxPayments
                .Where(t => !t.IsPaid && t.DueDate >= today && t.DueDate <= endDate)
                .GroupBy(t => t.DueDate.Date)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

            // Daily income from recent history
            var threeMonthsAgo = today.AddMonths(-3);
            var recentIncome = transactions.Where(t => t.Date >= threeMonthsAgo && t.Date <= today && t.Amount > 0).ToList();
            var totalDays = Math.Max((today - threeMonthsAgo).Days, 1);
            var baseDailyIncome = recentIncome.Sum(t => t.Amount) / totalDays * multiplier;

            var recentExpense = transactions.Where(t => t.Date >= threeMonthsAgo && t.Date <= today && t.Amount < 0).ToList();
            var baseDailyExpense = Math.Abs(recentExpense.Sum(t => t.Amount)) / totalDays * multiplier;

            decimal balance = currentBalance;
            for (int i = 1; i <= days; i++)
            {
                var d = today.AddDays(i);
                var month = d.Month;

                // Seasonal adjustment
                var seasonalIncFactor = seasonalIncome[month] > 0 ? seasonalIncome[month] / (baseDailyIncome * 30 + 1) : 1;
                var seasonalExpFactor = seasonalExpense[month] > 0 ? seasonalExpense[month] / (baseDailyExpense * 30 + 1) : 1;

                // Blend: 70% base + 30% seasonal
                var dailyIncome = (baseDailyIncome * 0.7m + baseDailyIncome * seasonalIncFactor * 0.3m);
                var dailyExpense = (baseDailyExpense * 0.7m + baseDailyExpense * seasonalExpFactor * 0.3m);

                // Add trend (per month, so divide by 30 for daily)
                dailyIncome += incomeTrend / 30;
                dailyExpense += expenseTrend / 30;

                dailyIncome = Math.Max(0, dailyIncome);
                dailyExpense = Math.Max(0, dailyExpense);

                balance += dailyIncome;
                balance -= dailyExpense;

                // Subtract reminders
                if (reminderByDate.TryGetValue(d.Date, out var remAmount))
                    balance -= remAmount;

                // Subtract tax payments
                if (taxByDate.TryGetValue(d.Date, out var taxAmount))
                    balance -= taxAmount;

                points.Add(new ScenarioPoint
                {
                    Date = d.Date,
                    Income = Math.Round(dailyIncome, 2),
                    Expense = Math.Round(dailyExpense, 2),
                    Balance = Math.Round(balance, 2),
                    IsWeekend = d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday
                });
            }

            var totalIncome = points.Sum(p => p.Income);
            var totalExpense = points.Sum(p => p.Expense);
            var finalBalance = points.Any() ? points.Last().Balance : currentBalance;

            return new CashScenario
            {
                Name = multiplier == 1.0m ? "Базовый" : multiplier > 1.0m ? "Оптимистичный" : "Пессимистичный",
                Points = points,
                TotalIncome = Math.Round(totalIncome, 2),
                TotalExpense = Math.Round(totalExpense, 2),
                FinalBalance = Math.Round(finalBalance, 2)
            };
        }

        private List<CashGap> DetectGapsInForecast(List<ScenarioPoint> points, decimal threshold)
        {
            var gaps = new List<CashGap>();
            CashGap? currentGap = null;

            foreach (var p in points)
            {
                if (p.Balance < threshold)
                {
                    if (currentGap == null)
                    {
                        currentGap = new CashGap
                        {
                            StartDate = p.Date,
                            MinBalance = p.Balance
                        };
                    }
                    else
                    {
                        currentGap.EndDate = p.Date;
                        if (p.Balance < currentGap.MinBalance)
                            currentGap.MinBalance = p.Balance;
                    }
                }
                else if (currentGap != null)
                {
                    currentGap.EndDate = currentGap.EndDate ?? currentGap.StartDate;
                    currentGap.Duration = (currentGap.EndDate.Value - currentGap.StartDate).Days + 1;
                    currentGap.Severity = CalculateGapSeverity(currentGap.MinBalance, threshold);
                    gaps.Add(currentGap);
                    currentGap = null;
                }
            }

            if (currentGap != null)
            {
                currentGap.EndDate = points.Last().Date;
                currentGap.Duration = (currentGap.EndDate.Value - currentGap.StartDate).Days + 1;
                currentGap.Severity = CalculateGapSeverity(currentGap.MinBalance, threshold);
                gaps.Add(currentGap);
            }

            return gaps;
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

    public class CashGap
    {
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal MinBalance { get; set; }
        public int Duration { get; set; }
        public GapSeverity Severity { get; set; }
    }

    public enum GapSeverity
    {
        None,
        Low,
        Medium,
        High,
        Critical
    }

    // Advanced forecasting models
    public class AdvancedCashForecast
    {
        public decimal CurrentBalance { get; set; }
        public CashScenario BaseScenario { get; set; } = new();
        public CashScenario OptimisticScenario { get; set; } = new();
        public CashScenario PessimisticScenario { get; set; } = new();
        public List<CashGap> Gaps { get; set; } = new();
        public List<KeyDate> KeyDates { get; set; } = new();
        public decimal MinBalance { get; set; }
        public decimal MaxBalance { get; set; }
        public decimal AvgDailyIncome { get; set; }
        public decimal AvgDailyExpense { get; set; }
        public int DaysInRed { get; set; }
        public int RecoveryDays { get; set; }
        public bool HasRisk { get; set; }
        public string RiskLevel { get; set; } = "safe";
    }

    public class CashScenario
    {
        public string Name { get; set; } = string.Empty;
        public List<ScenarioPoint> Points { get; set; } = new();
        public decimal TotalIncome { get; set; }
        public decimal TotalExpense { get; set; }
        public decimal FinalBalance { get; set; }
    }

    public class ScenarioPoint
    {
        public DateTime Date { get; set; }
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public decimal Balance { get; set; }
        public bool IsWeekend { get; set; }
    }

    public class KeyDate
    {
        public DateTime Date { get; set; }
        public string Label { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Type { get; set; } = string.Empty;
    }
}

using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class ExtendedReportService : IExtendedReportService
{
    private static readonly string[] RuMonths =
    [
        "", "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря"
    ];

    private static readonly string[] RuWeekdays = ["воскресенье", "понедельник", "вторник", "среда", "четверг", "пятница", "суббота"];
    private static readonly string[] RuWeekdaysShort = ["Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб"];

    private readonly ApplicationDbContext _db;
    private readonly IForecastingService _forecasting;
    private readonly IDataScopeService _dataScope;

    public ExtendedReportService(ApplicationDbContext db, IForecastingService forecasting, IDataScopeService dataScope)
    {
        _db = db;
        _forecasting = forecasting;
        _dataScope = dataScope;
    }

    public async Task<ExtendedAnalyticsDto> BuildAsync(string userId, ReportFilters filters, string? counterparty = null)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        var allTx = await _db.Transactions
            .Include(t => t.Project)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        var periodTx = Filter(allTx, filters);
        var reminders = await _db.Reminders.Where(r => r.UserId == userId && !r.IsPaid).ToListAsync();
        var taxPayments = await _db.TaxPayments.Where(t => t.UserId == userId).ToListAsync();

        return new ExtendedAnalyticsDto
        {
            Forecast = BuildForecastInsights(allTx, reminders, taxPayments, filters),
            Tax = BuildTaxReport(periodTx, taxPayments, filters),
            Counterparty = BuildCounterpartyReport(periodTx, counterparty),
            PeriodComparison = BuildPeriodComparison(allTx, filters),
            WeekdayExpenses = BuildWeekdayExpenses(periodTx)
        };
    }

    private static List<Transaction> Filter(List<Transaction> source, ReportFilters f)
    {
        var q = source.Where(t => t.Date >= f.Start && t.Date <= f.End);
        if (f.ProjectId.HasValue)
            q = q.Where(t => t.ProjectId == f.ProjectId);
        if (f.Categories.Count > 0)
        {
            var set = new HashSet<string>(f.Categories, StringComparer.OrdinalIgnoreCase);
            q = q.Where(t => set.Contains(t.Category));
        }
        return q.ToList();
    }

    private ForecastInsightsDto BuildForecastInsights(
        List<Transaction> allTx,
        List<Reminder> reminders,
        List<TaxPayment> taxPayments,
        ReportFilters filters)
    {
        var days = filters.ForecastDays switch { 30 => 30, 60 => 60, _ => 90 };
        var today = DateTime.Today;
        var advanced = _forecasting.ForecastCashGapsAdvanced(allTx, reminders, taxPayments, days);
        var currentBalance = allTx.Where(t => t.Date <= today).Sum(t => t.Amount);

        var monthly = allTx
            .Where(t => t.Date < today)
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new
            {
                Month = new DateTime(g.Key.Year, g.Key.Month, 1),
                Income = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                Expense = Math.Abs(g.Where(t => t.Amount < 0).Sum(t => t.Amount))
            })
            .ToList();

        var incomeHistory = monthly.Select(m => new MonthlyPointDto
        {
            Month = m.Month,
            Label = m.Month.ToString("MMM yyyy", RuCulture),
            Value = m.Income
        }).ToList();

        var expenseHistory = monthly.Select(m => new MonthlyPointDto
        {
            Month = m.Month,
            Label = m.Month.ToString("MMM yyyy", RuCulture),
            Value = m.Expense
        }).ToList();

        var incomes = monthly.Select(m => m.Income).Where(v => v > 0).ToList();
        var meanIncome = incomes.Any() ? incomes.Average() : 0;
        var medianIncome = incomes.Any() ? Median(incomes) : 0;
        var trendIncome = ComputeTrendProjection(monthly.Select(m => m.Income).ToList());

        var recommendedMethod = "mean";
        var recommendedValue = meanIncome;
        if (monthly.Count >= 4)
        {
            var last3 = monthly.TakeLast(3).Select(m => m.Income).ToList();
            var volatility = StdDev(last3);
            if (volatility > meanIncome * 0.4m)
            {
                recommendedMethod = "median";
                recommendedValue = medianIncome;
            }
            else if (trendIncome > meanIncome * 1.05m || trendIncome < meanIncome * 0.95m)
            {
                recommendedMethod = "trend";
                recommendedValue = trendIncome;
            }
        }

        var expenses = monthly.Select(m => m.Expense).Where(v => v > 0).ToList();
        var baseExpense = expenses.Any() ? expenses.Average() : 0;
        var nextMonth = today.AddMonths(1);
        var monthlyTyped = monthly.Select(m => (m.Month, m.Income, m.Expense)).ToList();
        var seasonalPattern = BuildSeasonalPattern(monthlyTyped);
        var seasonalFactor = seasonalPattern.FirstOrDefault(s => s.Month == nextMonth.Month)?.IndexPercent ?? 100;
        var nextMonthExpense = baseExpense * (seasonalFactor / 100m);

        var targetDate = today.AddDays(Math.Min(days, 60));
        var projectedBalance = advanced.BaseScenario.Points
            .FirstOrDefault(p => p.Date >= targetDate)?.Balance
            ?? advanced.BaseScenario.FinalBalance;

        var targetLabel = $"{targetDate.Day} {RuMonths[targetDate.Month]}";
        var balanceMessage = projectedBalance >= 0
            ? $"Если всё пойдёт по плану, {targetLabel} у вас будет {projectedBalance:N0} BYN на счёте."
            : $"По базовому сценарию, {targetLabel} возможен дефицит {Math.Abs(projectedBalance):N0} BYN — стоит заранее подготовить резерв.";

        var whatIf = new List<WhatIfScenarioDto>
        {
            BuildWhatIf("base", "Базовый сценарий", "Текущий тренд и сезонность", 1m, 1m, advanced.BaseScenario.FinalBalance),
            BuildWhatIf("optimistic", "Оптимистичный", "Доходы выше на 20%", 1.2m, 1m, advanced.OptimisticScenario.FinalBalance),
            BuildWhatIf("pessimistic", "Пессимистичный", "Доходы ниже на 20%", 0.8m, 1m, advanced.PessimisticScenario.FinalBalance),
            BuildWhatIf("income_down", "Доход −20%", "Что если доход упадёт на 20%", 0.8m, 1m,
                SimulateWhatIf(allTx, reminders, taxPayments, days, 0.8m, 1m)),
            BuildWhatIf("expense_up", "Расход +30%", "Что если расходы вырастут на 30%", 1m, 1.3m,
                SimulateWhatIf(allTx, reminders, taxPayments, days, 1m, 1.3m)),
            BuildWhatIf("stress", "Стресс-тест", "Доход −20% и расход +30%", 0.8m, 1.3m,
                SimulateWhatIf(allTx, reminders, taxPayments, days, 0.8m, 1.3m))
        };

        var anomalies = DetectAnomalies(allTx.Where(t => t.Date >= today.AddMonths(-12)).ToList());

        return new ForecastInsightsDto
        {
            HorizonDays = days,
            Income = new IncomeForecastDto
            {
                MeanMonthly = meanIncome,
                MedianMonthly = medianIncome,
                TrendMonthly = trendIncome,
                RecommendedMethod = recommendedMethod,
                RecommendedValue = recommendedValue,
                History = incomeHistory
            },
            Expense = new ExpenseForecastDto
            {
                NextMonthForecast = nextMonthExpense,
                BaseMonthly = baseExpense,
                SeasonalPattern = seasonalPattern,
                History = expenseHistory
            },
            BalanceProjection = new BalanceProjectionDto
            {
                TargetDate = targetDate,
                ProjectedBalance = projectedBalance,
                CurrentBalance = currentBalance,
                Message = balanceMessage
            },
            WhatIfScenarios = whatIf,
            Anomalies = anomalies
        };
    }

    private static readonly System.Globalization.CultureInfo RuCulture = new("ru-RU");

    private WhatIfScenarioDto BuildWhatIf(string key, string name, string desc, decimal inc, decimal exp, decimal balance) =>
        new()
        {
            Key = key,
            Name = name,
            Description = desc,
            IncomeMultiplier = inc,
            ExpenseMultiplier = exp,
            ProjectedBalance = balance,
            HasRisk = balance < 0
        };

    private decimal SimulateWhatIf(
        List<Transaction> allTx,
        List<Reminder> reminders,
        List<TaxPayment> taxPayments,
        int days,
        decimal incomeMult,
        decimal expenseMult)
    {
        var today = DateTime.Today;
        var balance = allTx.Where(t => t.Date <= today).Sum(t => t.Amount);
        var threeMonthsAgo = today.AddMonths(-3);
        var span = Math.Max((today - threeMonthsAgo).Days, 1);
        var dailyIncome = allTx.Where(t => t.Date >= threeMonthsAgo && t.Date <= today && t.Amount > 0).Sum(t => t.Amount) / span * incomeMult;
        var dailyExpense = Math.Abs(allTx.Where(t => t.Date >= threeMonthsAgo && t.Date <= today && t.Amount < 0).Sum(t => t.Amount)) / span * expenseMult;

        var reminderMap = reminders.Where(r => !r.IsPaid && r.Date > today && r.Date <= today.AddDays(days))
            .GroupBy(r => r.Date.Date).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        var taxMap = taxPayments.Where(t => !t.IsPaid && t.DueDate > today && t.DueDate <= today.AddDays(days))
            .GroupBy(t => t.DueDate.Date).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        for (int i = 1; i <= days; i++)
        {
            var d = today.AddDays(i);
            balance += dailyIncome;
            balance -= dailyExpense;
            if (reminderMap.TryGetValue(d.Date, out var r)) balance -= r;
            if (taxMap.TryGetValue(d.Date, out var t)) balance -= t;
        }
        return Math.Round(balance, 2);
    }

    private List<SeasonalMonthDto> BuildSeasonalPattern(List<(DateTime Month, decimal Income, decimal Expense)> monthly)
    {
        var overall = monthly.Any() ? monthly.Average(m => m.Expense) : 0;
        if (overall <= 0) overall = 1;

        return Enumerable.Range(1, 12).Select(m =>
        {
            var data = monthly.Where(x => x.Month.Month == m).ToList();
            var avg = data.Any() ? data.Average(x => x.Expense) : overall;
            return new SeasonalMonthDto
            {
                Month = m,
                MonthName = RuCulture.DateTimeFormat.GetMonthName(m),
                AvgExpense = avg,
                IndexPercent = Math.Round(avg / overall * 100, 0)
            };
        }).ToList();
    }

    private TaxReportDto BuildTaxReport(List<Transaction> periodTx, List<TaxPayment> taxPayments, ReportFilters filters)
    {
        var taxTx = periodTx.Where(t => CategoryBucketHelper.IsTax(t.Category) && t.Amount < 0).ToList();
        var accruedFromTx = Math.Abs(taxTx.Sum(t => t.Amount));

        var plannedInPeriod = taxPayments
            .Where(t => t.DueDate >= filters.Start && t.DueDate <= filters.End)
            .ToList();
        var accruedPlanned = plannedInPeriod.Sum(t => t.Amount);
        var paidPlanned = plannedInPeriod.Where(t => t.IsPaid).Sum(t => t.Amount);
        var paidFromTx = Math.Abs(taxTx.Where(t => t.IsConfirmed).Sum(t => t.Amount));

        var accrued = accruedPlanned > 0 ? accruedPlanned : accruedFromTx;
        var paid = paidPlanned > 0 ? paidPlanned : paidFromTx;

        var lines = taxTx
            .GroupBy(t => t.Category)
            .Select(g => new TaxReportLineDto
            {
                Name = g.Key,
                Amount = Math.Abs(g.Sum(t => t.Amount)),
                Date = g.Max(t => t.Date),
                IsPaid = true,
                Source = "операция"
            })
            .ToList();

        foreach (var tp in plannedInPeriod)
        {
            if (!lines.Any(l => l.Name.Equals(tp.Name, StringComparison.OrdinalIgnoreCase)))
                lines.Add(new TaxReportLineDto
                {
                    Name = tp.Name,
                    Amount = tp.Amount,
                    Date = tp.DueDate,
                    IsPaid = tp.IsPaid,
                    Source = "план"
                });
        }

        return new TaxReportDto
        {
            Accrued = accrued,
            Paid = paid,
            Remaining = Math.Max(0, accrued - paid),
            Lines = lines.OrderByDescending(l => l.Amount).ToList(),
            PlannedPayments = plannedInPeriod.Select(t => new TaxReportLineDto
            {
                Name = t.Name,
                Amount = t.Amount,
                Date = t.DueDate,
                IsPaid = t.IsPaid,
                Source = "налог"
            }).ToList()
        };
    }

    private CounterpartyReportDto BuildCounterpartyReport(List<Transaction> periodTx, string? selected)
    {
        var withCp = periodTx
            .Where(t => !string.IsNullOrWhiteSpace(t.Counterparty))
            .ToList();

        var top = withCp
            .GroupBy(t => t.Counterparty!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CounterpartySummaryDto
            {
                Name = g.Key,
                Income = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                Expense = Math.Abs(g.Where(t => t.Amount < 0).Sum(t => t.Amount)),
                Count = g.Count()
            })
            .OrderByDescending(c => c.Expense + c.Income)
            .Take(20)
            .ToList();

        var filtered = string.IsNullOrWhiteSpace(selected)
            ? withCp
            : withCp.Where(t => t.Counterparty!.Equals(selected, StringComparison.OrdinalIgnoreCase)).ToList();

        return new CounterpartyReportDto
        {
            SelectedCounterparty = selected,
            TopCounterparties = top,
            Transactions = filtered.OrderByDescending(t => t.Date).Select(t => new CounterpartyTransactionDto
            {
                Id = t.Id,
                Date = t.Date,
                Description = t.Description,
                Category = t.Category,
                Amount = t.Amount
            }).ToList(),
            TotalIncome = filtered.Where(t => t.Amount > 0).Sum(t => t.Amount),
            TotalExpense = Math.Abs(filtered.Where(t => t.Amount < 0).Sum(t => t.Amount)),
            Net = filtered.Sum(t => t.Amount)
        };
    }

    private PeriodComparisonDetailDto BuildPeriodComparison(List<Transaction> allTx, ReportFilters filters)
    {
        var (prevStart, prevEnd) = filters.GetComparisonPeriod();
        var current = Filter(allTx, filters);
        var prevF = new ReportFilters
        {
            Start = prevStart,
            End = prevEnd,
            ProjectId = filters.ProjectId,
            Categories = filters.Categories
        };
        var previous = Filter(allTx, prevF);

        decimal Rev(IEnumerable<Transaction> tx) => tx.Where(t => t.Amount > 0).Sum(t => t.Amount);
        decimal Exp(IEnumerable<Transaction> tx) => Math.Abs(tx.Where(t => t.Amount < 0).Sum(t => t.Amount));

        var metrics = new List<ComparisonMetricDto>
        {
            CompareMetric("revenue", "Выручка", Rev(current), Rev(previous), higherIsBetter: true),
            CompareMetric("expense", "Расходы", Exp(current), Exp(previous), higherIsBetter: false),
            CompareMetric("profit", "Чистая прибыль", Rev(current) - Exp(current), Rev(previous) - Exp(previous), higherIsBetter: true),
            CompareMetric("tx", "Операций", current.Count, previous.Count, higherIsBetter: true)
        };

        return new PeriodComparisonDetailDto
        {
            Mode = filters.CompareMode,
            CurrentStart = filters.Start,
            CurrentEnd = filters.End,
            PreviousStart = prevStart,
            PreviousEnd = prevEnd,
            Metrics = metrics
        };
    }

    private static ComparisonMetricDto CompareMetric(string key, string label, decimal cur, decimal prev, bool higherIsBetter)
    {
        var change = prev == 0 ? (cur == 0 ? 0 : 100) : ((cur - prev) / Math.Abs(prev)) * 100;
        var trend = change > 0.5m ? "up" : change < -0.5m ? "down" : "flat";
        if (!higherIsBetter) trend = trend == "up" ? "down" : trend == "down" ? "up" : "flat";
        return new ComparisonMetricDto
        {
            Key = key,
            Label = label,
            Current = cur,
            Previous = prev,
            ChangePercent = Math.Round(change, 1),
            Trend = trend
        };
    }

    private WeekdayExpenseDto BuildWeekdayExpenses(List<Transaction> periodTx)
    {
        var expenses = periodTx.Where(t => t.Amount < 0).ToList();
        var buckets = Enumerable.Range(0, 7).Select(dow =>
        {
            var dayTx = expenses.Where(t => (int)t.Date.DayOfWeek == dow).ToList();
            var total = Math.Abs(dayTx.Sum(t => t.Amount));
            var count = dayTx.Count;
            return new WeekdayBucketDto
            {
                DayOfWeek = dow,
                DayName = RuWeekdays[dow],
                DayShort = RuWeekdaysShort[dow],
                TotalExpense = total,
                TransactionCount = count,
                AvgExpense = count > 0 ? total / count : 0
            };
        }).ToList();

        var max = buckets.Any() ? buckets.Max(b => b.TotalExpense) : 0;
        if (max > 0)
        {
            buckets = buckets.Select(b => new WeekdayBucketDto
            {
                DayOfWeek = b.DayOfWeek,
                DayName = b.DayName,
                DayShort = b.DayShort,
                TotalExpense = b.TotalExpense,
                TransactionCount = b.TransactionCount,
                AvgExpense = b.AvgExpense,
                Intensity = (int)Math.Round(b.TotalExpense / max * 100)
            }).ToList();
        }

        var peak = buckets.OrderByDescending(b => b.TotalExpense).FirstOrDefault();

        return new WeekdayExpenseDto
        {
            Days = buckets,
            MaxAmount = max,
            PeakDay = peak?.DayName ?? ""
        };
    }

    private List<ExpenseAnomalyDto> DetectAnomalies(List<Transaction> transactions)
    {
        var expenses = transactions.Where(t => t.Amount < 0).ToList();
        if (expenses.Count < 5) return new List<ExpenseAnomalyDto>();

        var byCategory = expenses.GroupBy(t => t.Category);
        var anomalies = new List<ExpenseAnomalyDto>();

        foreach (var group in byCategory)
        {
            var amounts = group.Select(t => Math.Abs(t.Amount)).OrderBy(a => a).ToList();
            var median = Median(amounts);
            var mean = amounts.Average();
            var std = StdDev(amounts);
            var threshold = Math.Max(median * 3, mean + std * 2);

            foreach (var t in group.Where(t => Math.Abs(t.Amount) >= threshold).OrderByDescending(t => Math.Abs(t.Amount)).Take(3))
            {
                var amt = Math.Abs(t.Amount);
                anomalies.Add(new ExpenseAnomalyDto
                {
                    TransactionId = t.Id,
                    Date = t.Date,
                    Description = t.Description,
                    Category = t.Category,
                    Amount = amt,
                    ExpectedAmount = median,
                    Reason = amt >= median * 3
                        ? $"Сумма в {amt / Math.Max(median, 1):F0}× выше типичной для «{t.Category}»"
                        : "Значительно выше среднего по категории",
                    Severity = amt >= threshold * 1.5m ? "high" : "medium"
                });
            }
        }

        return anomalies.OrderByDescending(a => a.Amount).Take(10).ToList();
    }

    private static decimal Median(List<decimal> values)
    {
        if (!values.Any()) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        return n % 2 == 0 ? (sorted[n / 2 - 1] + sorted[n / 2]) / 2 : sorted[n / 2];
    }

    private static decimal StdDev(List<decimal> values)
    {
        if (values.Count < 2) return 0;
        var avg = values.Average();
        var sumSq = values.Sum(v => (v - avg) * (v - avg));
        return (decimal)Math.Sqrt((double)(sumSq / values.Count));
    }

    private static decimal ComputeTrendProjection(List<decimal> series)
    {
        if (series.Count < 2) return series.LastOrDefault();
        var n = series.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += (double)series[i];
            sumXY += i * (double)series[i];
            sumXX += i * i;
        }
        var denom = n * sumXX - sumX * sumX;
        if (denom == 0) return series.Last();
        var slope = (n * sumXY - sumX * sumY) / denom;
        var intercept = (sumY - slope * sumX) / n;
        return (decimal)(intercept + slope * n);
    }
}

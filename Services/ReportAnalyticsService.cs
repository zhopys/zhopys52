using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public class ReportAnalyticsService : IReportAnalyticsService
{
    private static readonly string[] ChartColors =
    [
        "#ef4444", "#f59e0b", "#3b82f6", "#8b5cf6", "#10b981",
        "#ec4899", "#06b6d4", "#94a3b8"
    ];

    private readonly ApplicationDbContext _db;
    private readonly IReportService _reportService;
    private readonly IForecastingService _forecastingService;
    private readonly ITransactionDataStatusService _dataStatusService;
    private readonly IAccountingIntegrationService _accountingService;
    private readonly ICategorizationService _categorizationService;
    private readonly IDataScopeService _dataScope;
    private readonly IUserContextService _userContext;

    public ReportAnalyticsService(
        ApplicationDbContext db,
        IReportService reportService,
        IForecastingService forecastingService,
        ITransactionDataStatusService dataStatusService,
        IAccountingIntegrationService accountingService,
        ICategorizationService categorizationService,
        IDataScopeService dataScope,
        IUserContextService userContext)
    {
        _db = db;
        _reportService = reportService;
        _forecastingService = forecastingService;
        _dataStatusService = dataStatusService;
        _accountingService = accountingService;
        _categorizationService = categorizationService;
        _dataScope = dataScope;
        _userContext = userContext;
    }

    public async Task<ReportAnalyticsSnapshot> BuildSnapshotAsync(string userId, ReportFilters filters)
    {
        userId = await ServiceDataScope.ResolveAsync(_dataScope, userId);
        filters.ForecastDays = filters.ForecastDays switch { 30 => 30, 60 => 60, _ => 90 };

        var ctx = await _userContext.GetContextAsync(userId);
        var allTransactions = await _userContext
            .FilterTransactionsForRole(
                _db.Transactions.Include(t => t.Project).Where(t => t.UserId == userId),
                ctx)
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        var periodTx = ApplyFilters(allTransactions, filters);
        List<Transaction> prevTx;
        if (filters.CompareMode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            prevTx = periodTx;
        }
        else
        {
            var (prevStart, prevEnd) = filters.GetComparisonPeriod();
            var prevFilters = new ReportFilters
            {
                Start = prevStart,
                End = prevEnd,
                PeriodType = filters.PeriodType,
                ProjectId = filters.ProjectId,
                Categories = filters.Categories,
                Department = filters.Department,
                CompareMode = filters.CompareMode
            };
            prevTx = ApplyFilters(allTransactions, prevFilters);
        }

        var reminders = await _db.Reminders.Where(r => r.UserId == userId && !r.IsPaid).ToListAsync();
        var taxPayments = await _db.TaxPayments.Where(t => t.UserId == userId && !t.IsPaid).ToListAsync();
        var projects = await _db.Projects.Where(p => p.UserId == userId || p.UserId == null).ToListAsync();

        if (!string.IsNullOrWhiteSpace(filters.Department))
            projects = projects.Where(p => p.Department == filters.Department).ToList();

        var taxReserve = filters.IncludeTaxReserve
            ? taxPayments.Where(t => t.DueDate <= filters.End.AddDays(filters.ForecastDays)).Sum(t => t.Amount)
              + reminders.Where(r => r.Date <= filters.End.AddDays(filters.ForecastDays)).Sum(r => r.Amount)
            : 0;

        var orgSettings = await _db.OrganizationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId);
        var minCashThreshold = orgSettings?.MinCashBalance ?? 1000;

        var currentBalance = allTransactions.Where(t => t.Date <= DateTime.Today).Sum(t => t.Amount);
        var freeCash = currentBalance - taxReserve;

        var kpi = BuildKpi(periodTx, prevTx, projects, freeCash);
        var catReport = _reportService.GetCategoryBreakdown(periodTx);
        var expenseChart = BuildExpenseChart(catReport, filters.Categories.FirstOrDefault());

        return new ReportAnalyticsSnapshot
        {
            Filters = filters,
            SelectedCategory = filters.Categories.FirstOrDefault(),
            Kpi = kpi,
            TaxReserve = taxReserve,
            FreeCash = freeCash,
            DataStatus = await _dataStatusService.GetStatusAsync(userId, filters.Start, filters.End),
            Accounting = await _accountingService.GetStatusAsync(userId),
            CriticalReminders = BuildCriticalAlerts(reminders, taxPayments),
            ExpenseChart = expenseChart,
            ProfitLoss = _reportService.GetProfitLossReport(periodTx, filters.Start, filters.End),
            CashFlow = _reportService.GetCashFlowStatement(periodTx, filters.Start, filters.End),
            TrialBalance = _reportService.GetTrialBalanceReport(periodTx, filters.Start, filters.End),
            IncomeExpenseBook = _reportService.GetIncomeExpenseBook(periodTx, filters.Start, filters.End),
            MonthlyCashflow = BuildMonthlyCashflow(allTransactions, filters.End),
            Projects = BuildProjectRows(periodTx, projects),
            ProfitabilityMatrix = BuildProfitabilityMatrix(periodTx, projects),
            Forecast = BuildForecast(allTransactions, reminders, taxPayments, filters.ForecastDays, taxReserve, minCashThreshold),
            UncategorizedTransactions = BuildUncategorized(periodTx)
        };
    }

    private static List<Transaction> ApplyFilters(List<Transaction> source, ReportFilters filters)
    {
        var q = source.Where(t => t.Date >= filters.Start && t.Date <= filters.End);
        if (filters.ProjectId.HasValue)
            q = q.Where(t => t.ProjectId == filters.ProjectId);
        if (filters.Categories.Count > 0)
        {
            var set = new HashSet<string>(filters.Categories, StringComparer.OrdinalIgnoreCase);
            q = q.Where(t => set.Contains(t.Category));
        }
        return q.ToList();
    }

    private static List<KpiMetricDto> BuildKpi(
        List<Transaction> current,
        List<Transaction> previous,
        List<Project> projects,
        decimal freeCash)
    {
        decimal Rev(IEnumerable<Transaction> tx) => tx.Where(t => t.Amount > 0).Sum(t => t.Amount);
        decimal Exp(IEnumerable<Transaction> tx) => Math.Abs(tx.Where(t => t.Amount < 0).Sum(t => t.Amount));
        decimal Capex(IEnumerable<Transaction> tx) =>
            Math.Abs(tx.Where(t => t.Amount < 0 && CategoryBucketHelper.IsCapex(t.Category)).Sum(t => t.Amount));
        decimal Opex(IEnumerable<Transaction> tx) => Exp(tx) - Capex(tx);

        var revenue = Rev(current);
        var revPrev = Rev(previous);
        var opex = Opex(current);
        var opexPrev = Opex(previous);
        var capex = Capex(current);
        var capexPrev = Capex(previous);
        var net = revenue - Exp(current);
        var netPrev = revPrev - Exp(previous);
        var ebitda = revenue - opex;
        var ebitdaPrev = revPrev - opexPrev;

        var avgRoi = projects.Where(p => p.TargetROI.HasValue && p.Budget > 0).Select(p => p.TargetROI!.Value).DefaultIfEmpty(0).Average();

        return
        [
            Metric("revenue", revenue, revPrev),
            Metric("opex", opex, opexPrev),
            Metric("capex", capex, capexPrev),
            Metric("netProfit", net, netPrev),
            Metric("ebitda", ebitda, ebitdaPrev),
            Metric("roi", avgRoi, avgRoi, "percent"),
            Metric("freeCash", freeCash, freeCash)
        ];
    }

    private static KpiMetricDto Metric(string key, decimal value, decimal prev, string format = "currency")
    {
        var (label, hint) = ReportKpiLabels.Get(key);
        var change = prev == 0 ? (value == 0 ? 0 : 100) : ((value - prev) / Math.Abs(prev)) * 100;
        var trend = change > 0.5m ? "up" : change < -0.5m ? "down" : "flat";
        if (key is "opex" or "capex") trend = change > 0.5m ? "down" : change < -0.5m ? "up" : "flat";
        return new KpiMetricDto
        {
            Key = key,
            Label = label,
            Hint = hint,
            Value = value,
            PreviousValue = prev,
            ChangePercent = Math.Round(change, 1),
            Trend = trend,
            Format = format
        };
    }

    private static CategoryBreakdownChartDto BuildExpenseChart(CategoryReport report, string? highlight)
    {
        var items = report.ExpenseByCategory.OrderByDescending(c => c.Amount).Take(8).ToList();
        if (!items.Any()) return new CategoryBreakdownChartDto();
        return new CategoryBreakdownChartDto
        {
            Labels = items.Select(i => i.Category).ToList(),
            Values = items.Select(i => i.Percentage).ToList(),
            Amounts = items.Select(i => i.Amount).ToList(),
            Colors = items.Select((item, i) =>
                highlight != null && item.Category.Equals(highlight, StringComparison.OrdinalIgnoreCase)
                    ? "#00d4aa"
                    : ChartColors[i % ChartColors.Length]).ToList()
        };
    }

    private static List<CashflowEntry> BuildMonthlyCashflow(List<Transaction> transactions, DateTime end)
    {
        var monthlyTx = transactions
            .Where(t => t.Date <= end)
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new CashflowEntry
            {
                Date = new DateTime(g.Key.Year, g.Key.Month, 1),
                Income = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                Expense = Math.Abs(g.Where(t => t.Amount < 0).Sum(t => t.Amount)),
                Balance = g.Sum(t => t.Amount)
            }).ToList();
        decimal running = 0;
        foreach (var e in monthlyTx) { running += e.Balance; e.Balance = running; }
        return monthlyTx;
    }

    private List<ProjectProfitRowDto> BuildProjectRows(List<Transaction> periodTx, List<Project> projects)
    {
        var summaries = _reportService.GetProjectReport(periodTx).ToDictionary(s => s.Project, StringComparer.OrdinalIgnoreCase);
        var rows = new List<ProjectProfitRowDto>();
        foreach (var p in projects.Where(p => !p.IsDefault))
        {
            summaries.TryGetValue(p.Name, out var s);
            var income = s?.Income ?? 0;
            var expense = s?.Expense ?? 0;
            rows.Add(new ProjectProfitRowDto
            {
                ProjectId = p.Id,
                Name = p.Name,
                Department = p.Department,
                Income = income,
                Expense = expense,
                Profit = income - expense,
                Budget = p.Budget,
                TargetRoi = p.TargetROI,
                ActualRoi = p.Budget > 0 ? Math.Round((income - expense) / p.Budget.Value * 100, 1) : null,
                Transactions = s?.Transactions ?? 0
            });
        }
        return rows;
    }

    private static List<ProfitabilityMatrixRowDto> BuildProfitabilityMatrix(List<Transaction> periodTx, List<Project> projects)
    {
        var rows = new List<ProfitabilityMatrixRowDto>();
        foreach (var p in projects.Where(p => !p.IsDefault))
        {
            var tx = periodTx.Where(t => t.ProjectId == p.Id).ToList();
            if (!tx.Any() && !string.IsNullOrEmpty(p.Department))
                tx = periodTx.Where(t => t.Project?.Department == p.Department).ToList();

            var revenue = tx.Where(t => t.Amount > 0).Sum(t => t.Amount);
            var expenses = tx.Where(t => t.Amount < 0).ToList();
            var payroll = CategoryBucketHelper.SumExpenses(expenses.Select(e => (e.Category, e.Amount)), CategoryBucketHelper.IsPayroll);
            var rent = CategoryBucketHelper.SumExpenses(expenses.Select(e => (e.Category, e.Amount)), CategoryBucketHelper.IsRent);
            var taxes = CategoryBucketHelper.SumExpenses(expenses.Select(e => (e.Category, e.Amount)), CategoryBucketHelper.IsTax);
            var totalExp = Math.Abs(expenses.Sum(t => t.Amount));
            var net = revenue - totalExp;
            var margin = revenue - totalExp;

            rows.Add(new ProfitabilityMatrixRowDto
            {
                ProjectId = p.Id,
                Name = p.Name,
                Department = p.Department,
                Revenue = revenue,
                Margin = margin,
                MarginPercent = revenue > 0 ? Math.Round(margin / revenue * 100, 1) : 0,
                Payroll = payroll,
                Rent = rent,
                Taxes = taxes,
                NetIncome = net
            });
        }
        return rows.OrderByDescending(r => r.NetIncome).ToList();
    }

    private static List<TaxAlertDto> BuildCriticalAlerts(List<Reminder> reminders, List<TaxPayment> taxPayments)
    {
        var alerts = new List<TaxAlertDto>();
        var horizon = DateTime.Today.AddDays(30);
        foreach (var r in reminders.Where(r => r.Date <= horizon).OrderBy(r => r.Date).Take(5))
        {
            var days = (r.Date.Date - DateTime.Today).Days;
            alerts.Add(new TaxAlertDto
            {
                Id = r.Id,
                Title = r.Name,
                DueDate = r.Date,
                Amount = r.Amount,
                Severity = days <= 3 ? "critical" : days <= 7 ? "warning" : "info",
                Message = days <= 7 ? $"Оплата {r.Name} до {r.Date:dd} числа" : $"{r.Name} — {r.Date:dd MMM}",
                Source = "reminder"
            });
        }
        foreach (var t in taxPayments.Where(t => t.DueDate <= horizon).OrderBy(t => t.DueDate).Take(3))
        {
            var days = (t.DueDate.Date - DateTime.Today).Days;
            alerts.Add(new TaxAlertDto
            {
                Id = t.Id,
                Title = t.Name,
                DueDate = t.DueDate,
                Amount = t.Amount,
                Severity = days <= 3 ? "critical" : days <= 7 ? "warning" : "info",
                Message = $"Внимание: {t.Name} до {t.DueDate:dd.MM}",
                Source = "tax"
            });
        }
        return alerts.OrderBy(a => a.DueDate).Take(6).ToList();
    }

    private List<UncategorizedTransactionDto> BuildUncategorized(List<Transaction> periodTx)
    {
        var weak = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", "Прочее", "Другое", "Не указано", "Uncategorized", "Разное" };
        return periodTx.Where(t => t.Amount < 0 && weak.Contains(t.Category.Trim()))
            .OrderByDescending(t => Math.Abs(t.Amount)).Take(8)
            .Select(t => new UncategorizedTransactionDto
            {
                Id = t.Id,
                Date = t.Date,
                Description = t.Description,
                Amount = t.Amount,
                Category = t.Category,
                SuggestedCategory = _categorizationService.CategorizeTransaction(t.Description, t.Amount)
            }).ToList();
    }

    private CashForecastChartDto BuildForecast(
        List<Transaction> transactions,
        List<Reminder> reminders,
        List<TaxPayment> taxPayments,
        int days,
        decimal taxReserve,
        decimal minCashThreshold)
    {
        var advanced = _forecastingService.ForecastCashGapsAdvanced(
            transactions, reminders, taxPayments, days, 0, 0, minCashThreshold);
        var points = advanced.BaseScenario.Points.Select(p => new ForecastChartPointDto
        {
            Date = p.Date,
            Balance = p.Balance,
            IsGap = p.Balance < 0 || (p.Balance - taxReserve) < 0
        }).ToList();

        return new CashForecastChartDto
        {
            HorizonDays = days,
            CurrentBalance = advanced.CurrentBalance,
            HasRisk = advanced.HasRisk || (advanced.CurrentBalance - taxReserve) < 0,
            RiskLevel = advanced.RiskLevel,
            MinBalance = advanced.MinBalance,
            MinCashThreshold = minCashThreshold,
            Points = points,
            Gaps = advanced.Gaps.Select(g => new CashGapDto
            {
                Start = g.StartDate,
                End = g.EndDate,
                MinBalance = g.MinBalance,
                Severity = g.Severity.ToString().ToLowerInvariant()
            }).ToList()
        };
    }
}

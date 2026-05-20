namespace MiniFinance.Services;

public sealed class ExtendedAnalyticsDto
{
    public ForecastInsightsDto Forecast { get; init; } = new();
    public TaxReportDto Tax { get; init; } = new();
    public CounterpartyReportDto Counterparty { get; init; } = new();
    public PeriodComparisonDetailDto PeriodComparison { get; init; } = new();
    public WeekdayExpenseDto WeekdayExpenses { get; init; } = new();
}

public sealed class ForecastInsightsDto
{
    public IncomeForecastDto Income { get; init; } = new();
    public ExpenseForecastDto Expense { get; init; } = new();
    public BalanceProjectionDto BalanceProjection { get; init; } = new();
    public IReadOnlyList<WhatIfScenarioDto> WhatIfScenarios { get; init; } = Array.Empty<WhatIfScenarioDto>();
    public IReadOnlyList<ExpenseAnomalyDto> Anomalies { get; init; } = Array.Empty<ExpenseAnomalyDto>();
    public int HorizonDays { get; init; } = 90;
}

public sealed class IncomeForecastDto
{
    public decimal MeanMonthly { get; init; }
    public decimal MedianMonthly { get; init; }
    public decimal TrendMonthly { get; init; }
    public string RecommendedMethod { get; init; } = "mean";
    public decimal RecommendedValue { get; init; }
    public IReadOnlyList<MonthlyPointDto> History { get; init; } = Array.Empty<MonthlyPointDto>();
}

public sealed class ExpenseForecastDto
{
    public decimal NextMonthForecast { get; init; }
    public decimal BaseMonthly { get; init; }
    public IReadOnlyList<SeasonalMonthDto> SeasonalPattern { get; init; } = Array.Empty<SeasonalMonthDto>();
    public IReadOnlyList<MonthlyPointDto> History { get; init; } = Array.Empty<MonthlyPointDto>();
}

public sealed class SeasonalMonthDto
{
    public int Month { get; init; }
    public string MonthName { get; init; } = "";
    public decimal AvgExpense { get; init; }
    public decimal IndexPercent { get; init; }
}

public sealed class MonthlyPointDto
{
    public DateTime Month { get; init; }
    public string Label { get; init; } = "";
    public decimal Value { get; init; }
}

public sealed class BalanceProjectionDto
{
    public DateTime TargetDate { get; init; }
    public decimal ProjectedBalance { get; init; }
    public decimal CurrentBalance { get; init; }
    public string Message { get; init; } = "";
}

public sealed class WhatIfScenarioDto
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public decimal IncomeMultiplier { get; init; } = 1m;
    public decimal ExpenseMultiplier { get; init; } = 1m;
    public decimal ProjectedBalance { get; init; }
    public bool HasRisk { get; init; }
}

public sealed class ExpenseAnomalyDto
{
    public int TransactionId { get; init; }
    public DateTime Date { get; init; }
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal ExpectedAmount { get; init; }
    public string Reason { get; init; } = "";
    public string Severity { get; init; } = "medium";
}

public sealed class TaxReportDto
{
    public decimal Accrued { get; init; }
    public decimal Paid { get; init; }
    public decimal Remaining { get; init; }
    public IReadOnlyList<TaxReportLineDto> Lines { get; init; } = Array.Empty<TaxReportLineDto>();
    public IReadOnlyList<TaxReportLineDto> PlannedPayments { get; init; } = Array.Empty<TaxReportLineDto>();
}

public sealed class TaxReportLineDto
{
    public string Name { get; init; } = "";
    public decimal Amount { get; init; }
    public DateTime? Date { get; init; }
    public bool IsPaid { get; init; }
    public string Source { get; init; } = "";
}

public sealed class CounterpartyReportDto
{
    public string? SelectedCounterparty { get; init; }
    public IReadOnlyList<CounterpartySummaryDto> TopCounterparties { get; init; } = Array.Empty<CounterpartySummaryDto>();
    public IReadOnlyList<CounterpartyTransactionDto> Transactions { get; init; } = Array.Empty<CounterpartyTransactionDto>();
    public decimal TotalIncome { get; init; }
    public decimal TotalExpense { get; init; }
    public decimal Net { get; init; }
}

public sealed class CounterpartySummaryDto
{
    public string Name { get; init; } = "";
    public decimal Income { get; init; }
    public decimal Expense { get; init; }
    public int Count { get; init; }
}

public sealed class CounterpartyTransactionDto
{
    public int Id { get; init; }
    public DateTime Date { get; init; }
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public decimal Amount { get; init; }
}

public sealed class PeriodComparisonDetailDto
{
    public string Mode { get; init; } = "mom";
    public DateTime CurrentStart { get; init; }
    public DateTime CurrentEnd { get; init; }
    public DateTime PreviousStart { get; init; }
    public DateTime PreviousEnd { get; init; }
    public IReadOnlyList<ComparisonMetricDto> Metrics { get; init; } = Array.Empty<ComparisonMetricDto>();
}

public sealed class ComparisonMetricDto
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public decimal Current { get; init; }
    public decimal Previous { get; init; }
    public decimal ChangePercent { get; init; }
    public string Trend { get; init; } = "flat";
}

public sealed class WeekdayExpenseDto
{
    public IReadOnlyList<WeekdayBucketDto> Days { get; init; } = Array.Empty<WeekdayBucketDto>();
    public decimal MaxAmount { get; init; }
    public string PeakDay { get; init; } = "";
}

public sealed class WeekdayBucketDto
{
    public int DayOfWeek { get; init; }
    public string DayName { get; init; } = "";
    public string DayShort { get; init; } = "";
    public decimal TotalExpense { get; init; }
    public int TransactionCount { get; init; }
    public decimal AvgExpense { get; init; }
    public int Intensity { get; init; }
}

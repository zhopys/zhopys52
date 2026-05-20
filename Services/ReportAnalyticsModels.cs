namespace MiniFinance.Services;

public sealed class ReportAnalyticsSnapshot
{
    public ReportFilters Filters { get; init; } = new();
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public string? SelectedCategory { get; init; }

    public IReadOnlyList<KpiMetricDto> Kpi { get; init; } = Array.Empty<KpiMetricDto>();
    public decimal TaxReserve { get; init; }
    public decimal FreeCash { get; init; }

    public DataStatusDto DataStatus { get; init; } = new();
    public AccountingIntegrationDto Accounting { get; init; } = new();
    public IReadOnlyList<TaxAlertDto> CriticalReminders { get; init; } = Array.Empty<TaxAlertDto>();
    public CategoryBreakdownChartDto ExpenseChart { get; init; } = new();
    public ProfitLossReport? ProfitLoss { get; init; }
    public CashFlowStatementReport? CashFlow { get; init; }
    public TrialBalanceReport? TrialBalance { get; init; }
    public List<IncomeExpenseBookEntry> IncomeExpenseBook { get; init; } = new();
    public List<CashflowEntry> MonthlyCashflow { get; init; } = new();
    public IReadOnlyList<ProjectProfitRowDto> Projects { get; init; } = Array.Empty<ProjectProfitRowDto>();
    public IReadOnlyList<ProfitabilityMatrixRowDto> ProfitabilityMatrix { get; init; } = Array.Empty<ProfitabilityMatrixRowDto>();
    public CashForecastChartDto Forecast { get; init; } = new();
    public IReadOnlyList<UncategorizedTransactionDto> UncategorizedTransactions { get; init; } = Array.Empty<UncategorizedTransactionDto>();
}

public sealed class KpiMetricDto
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Hint { get; init; }
    public decimal Value { get; init; }
    public decimal PreviousValue { get; init; }
    public decimal ChangePercent { get; init; }
    public string Trend { get; init; } = "flat";
    public string Format { get; init; } = "currency";
}

public sealed class ProfitabilityMatrixRowDto
{
    public int? ProjectId { get; init; }
    public string Name { get; init; } = "";
    public string? Department { get; init; }
    public decimal Revenue { get; init; }
    public decimal Margin { get; init; }
    public decimal MarginPercent { get; init; }
    public decimal Payroll { get; init; }
    public decimal Rent { get; init; }
    public decimal Taxes { get; init; }
    public decimal NetIncome { get; init; }
    public bool IsProfitable => NetIncome >= 0;
}

namespace MiniFinance.Services;

public sealed class ReportsDashboardDto
{
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
    public CashForecastChartDto Forecast { get; init; } = new();
    public IReadOnlyList<UncategorizedTransactionDto> UncategorizedTransactions { get; init; } = Array.Empty<UncategorizedTransactionDto>();
}

public sealed class DataStatusDto
{
    public bool HasData { get; init; }
    public string SourceLabel { get; init; } = "CSV-импорт и ручной ввод";
    public DateTime? LastUpdatedAt { get; init; }
    public int TotalTransactions { get; init; }
    public int PeriodTransactions { get; init; }
}

public sealed class AccountingIntegrationDto
{
    public string Provider { get; init; } = "Моё Дело";
    public bool IsConnected { get; init; }
    public DateTime? LastExportAt { get; init; }
    public bool CanForceExport { get; init; } = true;
    public string Status { get; init; } = "idle"; // idle | exporting | error
    public string? LastError { get; init; }
}

public sealed class TaxAlertDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTime DueDate { get; init; }
    public decimal Amount { get; init; }
    public string Severity { get; init; } = "warning"; // critical | warning | info
    public string Message { get; init; } = string.Empty;
    public string Source { get; init; } = "reminder"; // reminder | tax
}

public sealed class CategoryBreakdownChartDto
{
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
    public IReadOnlyList<decimal> Values { get; init; } = Array.Empty<decimal>();
    public IReadOnlyList<decimal> Amounts { get; init; } = Array.Empty<decimal>();
    public IReadOnlyList<string> Colors { get; init; } = Array.Empty<string>();
}

public sealed class ProjectProfitRowDto
{
    public int? ProjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Department { get; init; }
    public decimal Income { get; init; }
    public decimal Expense { get; init; }
    public decimal Profit { get; init; }
    public decimal? Budget { get; init; }
    public decimal? TargetRoi { get; init; }
    public decimal? ActualRoi { get; init; }
    public int Transactions { get; init; }
    public bool IsProfitable => Profit >= 0;
}

public sealed class CashForecastChartDto
{
    public int HorizonDays { get; init; } = 90;
    public decimal CurrentBalance { get; init; }
    public bool HasRisk { get; init; }
    public string RiskLevel { get; init; } = "safe";
    public decimal MinBalance { get; init; }
    public IReadOnlyList<ForecastChartPointDto> Points { get; init; } = Array.Empty<ForecastChartPointDto>();
    public IReadOnlyList<CashGapDto> Gaps { get; init; } = Array.Empty<CashGapDto>();
}

public sealed class ForecastChartPointDto
{
    public DateTime Date { get; init; }
    public decimal Balance { get; init; }
    public bool IsGap { get; init; }
}

public sealed class CashGapDto
{
    public DateTime Start { get; init; }
    public DateTime? End { get; init; }
    public decimal MinBalance { get; init; }
    public string Severity { get; init; } = "medium";
}

public sealed class UncategorizedTransactionDto
{
    public int Id { get; init; }
    public DateTime Date { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Category { get; init; } = string.Empty;
    public string? SuggestedCategory { get; init; }
}

public record PeriodRange(DateTime Start, DateTime End);

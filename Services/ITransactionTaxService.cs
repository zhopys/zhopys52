using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public enum TransactionTaxTreatment
{
    Excluded,
    TaxPayment,
    TaxableIncome,
    DeductibleExpense,
    NpdFromIndividual,
    NpdFromLegal
}

public sealed class TransactionTaxLine
{
    public int TransactionId { get; init; }
    public DateTime Date { get; init; }
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public string? Counterparty { get; init; }
    public decimal Amount { get; init; }
    public TransactionTaxTreatment Treatment { get; init; }
    public decimal TaxBase { get; init; }
    public decimal AccruedTax { get; init; }
    public string RateLabel { get; init; } = "";
    public string Note { get; init; } = "";
}

public sealed class TaxPeriodAnalysisDto
{
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public TaxSystem TaxSystem { get; init; }
    public TaxpayerKind TaxpayerKind { get; init; }

    public decimal TaxableIncome { get; init; }
    public decimal DeductibleExpenses { get; init; }
    public decimal IncomeFromIndividuals { get; init; }
    public decimal IncomeFromLegalEntities { get; init; }
    public decimal ExcludedAmount { get; init; }
    public int OperationCount { get; init; }
    public int ExcludedCount { get; init; }

    public decimal AccruedTaxTotal { get; init; }
    public TaxCalculationResult Calculation { get; init; } = new();
    public IReadOnlyList<TransactionTaxLine> Lines { get; init; } = Array.Empty<TransactionTaxLine>();

    public decimal Income => TaxableIncome + IncomeFromIndividuals + IncomeFromLegalEntities;
    public decimal Expenses => DeductibleExpenses;
    public decimal Profit => Math.Max(0, Income - Expenses);
}

public interface ITransactionTaxService
{
    Task<TaxPeriodAnalysisDto> AnalyzePeriodAsync(
        string ownerUserId,
        DateTime start,
        DateTime end,
        TaxSystem taxSystem,
        TaxpayerKind taxpayerKind,
        bool includeFsznEstimate = false);

    Task<TransactionTaxLine?> AnalyzeTransactionAsync(
        string ownerUserId,
        int transactionId,
        TaxSystem taxSystem,
        TaxpayerKind taxpayerKind);

    bool IsExcludedFromTaxBase(string? category, string? description);
    bool IsLegalEntityCounterparty(string? counterpartyName, string? taxId);
}

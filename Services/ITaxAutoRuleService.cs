using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public sealed class TaxRulePreview
{
    public TaxAutoRule Rule { get; init; } = new();
    public decimal Income { get; init; }
    public decimal Expenses { get; init; }
    public int OperationCount { get; init; }
    public decimal CalculatedAmount { get; init; }
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public DateTime DueDate { get; init; }
    /// <summary>Имя записи в TaxPayments при создании из правила.</summary>
    public string PlannedPaymentName { get; init; } = "";
    public string? Error { get; init; }
}

public sealed class TaxRuleGenerateResult
{
    public int CreatedCount { get; init; }
    public int SkippedCount { get; init; }
    public int ErrorCount { get; init; }
    public List<string> Messages { get; init; } = new();
}

public interface ITaxAutoRuleService
{
    Task<List<TaxAutoRule>> ListAsync(string userId);
    Task<TaxAutoRule> SaveAsync(TaxAutoRule rule, string userId);
    Task DeleteAsync(int id, string userId);
    Task<List<TaxRulePreview>> PreviewAsync(string userId, DateTime? referenceDate = null);
    Task<TaxRuleGenerateResult> GeneratePaymentsAsync(string userId, bool skipExisting = true, DateTime? referenceDate = null);
    Task<TaxRuleGenerateResult> CreatePaymentFromPreviewAsync(string userId, TaxRulePreview preview);
    Task EnsureDefaultRulesAsync(string userId, TaxSystem? taxSystem);
    Task SyncRulesForTaxSystemAsync(string userId, TaxSystem taxSystem, bool replaceExisting = false);
}

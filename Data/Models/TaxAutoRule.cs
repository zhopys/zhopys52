using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models;

public enum TaxRulePeriod
{
    Monthly = 0,
    Quarterly = 1,
    Yearly = 2
}

/// <summary>
/// Правило автоматического расчёта налога по формуле.
/// Переменные: income, expenses, profit (income − expenses).
/// Пример: income * 0.06, max(0, profit) * 0.15
/// </summary>
public class TaxAutoRule
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Название платежа в TaxPayments (УСН, НДС, ФСЗН…).</summary>
    [Required]
    [StringLength(80)]
    public string PaymentName { get; set; } = "УСН";

    [Required]
    [StringLength(300)]
    public string Formula { get; set; } = "income * 0.06";

    public TaxRulePeriod Period { get; set; } = TaxRulePeriod.Quarterly;

    /// <summary>День месяца срока уплаты (после окончания периода).</summary>
    [Range(1, 28)]
    public int DueDayOfMonth { get; set; } = 25;

    /// <summary>Смещение месяца срока относительно конца периода (0 = в месяце окончания периода).</summary>
    public int DueMonthOffset { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public int SortOrder { get; set; }
}

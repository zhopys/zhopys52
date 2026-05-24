namespace MiniFinance.Data.Models;

/// <summary>Режимы налогообложения малого бизнеса в Республике Беларусь (упрощённо для MiniFinance).</summary>
public enum TaxSystem
{
    /// <summary>УСН — в основном ~6% от выручки, квартальная декларация (ЮЛ).</summary>
    USN = 0,
    /// <summary>ОСН — подоходный (ИП) / налог на прибыль (ЮЛ) от разницы доходов и расходов.</summary>
    OSN = 1,
    /// <summary>НПД — налог на профессиональный доход (4% с физлиц, 8% с юрлиц и ИП).</summary>
    NPD = 2,
    /// <summary>Единый налог — фиксированная сумма по виду деятельности (задаётся вручную).</summary>
    UnifiedTax = 3
}

public enum TaxpayerKind
{
  LegalEntity = 0,
  IndividualEntrepreneur = 1
}

namespace MiniFinance.Services;

/// <summary>Категория по MCC и типу операции (выписка AKBB / белорусские банки).</summary>
public static class MccCategoryMapper
{
    private static readonly Dictionary<int, string> MccMap = new()
    {
        [5411] = "Продукты",
        [5812] = "Рестораны",
        [5814] = "Рестораны",
        [6012] = "Прочее",
        [6011] = "Прочее",
        [4900] = "Коммунальные услуги",
        [4215] = "Прочее",
        [9399] = "Налоги",
        [7999] = "Развлечения",
        [5651] = "Прочее",
        [5200] = "Прочее",
        [5300] = "Прочее",
        [5818] = "Подписки",
        [5992] = "Прочее",
        [7230] = "Прочее",
        [4111] = "Транспорт",
        [8094] = "Прочее",
        [9406] = "Прочее",
    };

    private static readonly (string Prefix, string Category)[] OperationTypeCategories =
    [
        ("Начисление Cash-back", CategoryDefaults.DefaultIncome),
        ("Капитализация", CategoryDefaults.DefaultIncome),
        ("Возвращенная покупка", CategoryDefaults.DefaultIncome),
        ("Пополнение наличными", CategoryDefaults.DefaultIncome),
        ("Перевод P2P", "Прочее"),
        ("Перевод средств", "Прочее"),
        ("Плата за перевод", "Прочее"),
        ("Выдача наличных", "Прочее"),
        ("Оплата товаров и услуг", "Прочее"),
    ];

    public static string ResolveCategory(int? mcc, string operationType, string? merchant, bool isIncome,
        ICategorizationService? categorization, string description, decimal signedAmount)
    {
        if (mcc.HasValue && MccMap.TryGetValue(mcc.Value, out var byMcc))
            return isIncome && byMcc != CategoryDefaults.DefaultIncome ? CategoryDefaults.DefaultIncome : byMcc;

        foreach (var (prefix, cat) in OperationTypeCategories)
        {
            if (operationType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return cat;
        }

        if (!string.IsNullOrWhiteSpace(merchant))
        {
            var m = merchant.ToLowerInvariant();
            if (m.Contains("erip") || m.Contains("ериp"))
                return "Коммунальные услуги";
            if (m.Contains("belbet"))
                return "Прочее";
        }

        return categorization?.CategorizeTransaction(description, signedAmount)
               ?? (isIncome ? CategoryDefaults.DefaultIncome : CategoryDefaults.DefaultExpense);
    }

    public static bool IsFeeOperationType(string operationType) =>
        operationType.Contains("Плата за перевод", StringComparison.OrdinalIgnoreCase)
        || operationType.Contains("комиссия", StringComparison.OrdinalIgnoreCase);
}

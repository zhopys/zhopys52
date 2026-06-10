using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public enum CashFlowActivity
{
    Operating,
    Investment,
    Financing
}

public static class CashFlowActivityHelper
{
    private static readonly string[] FinancingKeywords =
    [
        "кредит", "займ", "loan", "credit", "ипотек", "лизинг", "leasing",
        "процент по кредит", "погашение кредит", "дивиденд", "вклад", "заем"
    ];

    private static readonly string[] InvestmentKeywords =
    [
        "инвест", "capex", "капвлож", "основн средств", "недвижим",
        "оборудован", "ноутбук", "компьютер", "сервер", "транспорт", "машин",
        "внедрен", "лиценз", "покупка актива", "продажа актива"
    ];

    public static CashFlowActivity Classify(Transaction transaction)
    {
        var category = transaction.Category ?? "";
        var description = transaction.Description ?? "";
        return Classify(category, description);
    }

    public static CashFlowActivity Classify(string category, string description)
    {
        if (IsFinancing(category, description))
            return CashFlowActivity.Financing;
        if (IsInvestment(category, description))
            return CashFlowActivity.Investment;
        return CashFlowActivity.Operating;
    }

    public static string GetLabel(CashFlowActivity activity) => activity switch
    {
        CashFlowActivity.Investment => "Инвестиционная",
        CashFlowActivity.Financing => "Финансовая",
        _ => "Операционная"
    };

    public static string GetHint(CashFlowActivity activity) => activity switch
    {
        CashFlowActivity.Investment => "Покупка и продажа долгосрочных активов: оборудование, техника, внедрения",
        CashFlowActivity.Financing => "Кредиты, займы, погашение долга, проценты, дивиденды",
        _ => "Обычная деятельность: выручка, зарплаты, аренда, налоги, закупки, услуги"
    };

    public static string GetSlug(CashFlowActivity activity) => activity switch
    {
        CashFlowActivity.Investment => "investment",
        CashFlowActivity.Financing => "financing",
        _ => "operating"
    };

    public static bool TryParseSlug(string? slug, out CashFlowActivity parsed)
    {
        parsed = CashFlowActivity.Operating;
        if (string.IsNullOrWhiteSpace(slug)) return false;

        var key = slug.Trim().ToLowerInvariant();
        if (key is "operating" or "operational" or "операционная")
        {
            parsed = CashFlowActivity.Operating;
            return true;
        }
        if (key is "investment" or "invest" or "инвестиционная")
        {
            parsed = CashFlowActivity.Investment;
            return true;
        }
        if (key is "financing" or "finance" or "финансовая")
        {
            parsed = CashFlowActivity.Financing;
            return true;
        }
        return false;
    }

    private static bool IsFinancing(string category, string description) =>
        Match(category, FinancingKeywords) || Match(description, FinancingKeywords);

    private static bool IsInvestment(string category, string description) =>
        CategoryBucketHelper.IsCapex(category)
        || Match(category, InvestmentKeywords)
        || Match(description, InvestmentKeywords);

    private static bool Match(string text, string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.ToLowerInvariant();
        return keywords.Any(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}

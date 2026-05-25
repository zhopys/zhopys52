namespace MiniFinance.Services;

public static class TaxFormHelper
{
    private static readonly string[] KnownTypes = ["НДС", "УСН", "ФСЗН", "Подоходный"];

    public static (string TypeSelect, string CustomName) MapToFormFields(string lineName, string? suggestedPaymentName = null)
    {
        var type = InferType(lineName) ?? InferType(suggestedPaymentName ?? "");
        if (type != null)
            return (type, "");
        return ("Другое", string.IsNullOrWhiteSpace(lineName) ? "Налог" : lineName.Trim());
    }

    public static string ResolvePaymentName(string typeSelect, string customName) =>
        typeSelect == "Другое"
            ? (string.IsNullOrWhiteSpace(customName) ? "Налог" : customName.Trim())
            : typeSelect;

    public static bool IsKnownTypeName(string name) =>
        KnownTypes.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static string? InferType(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Contains("НДС", StringComparison.OrdinalIgnoreCase)) return "НДС";
        if (text.Contains("УСН", StringComparison.OrdinalIgnoreCase)) return "УСН";
        if (text.Contains("ФСЗН", StringComparison.OrdinalIgnoreCase)
            || text.Contains("соц", StringComparison.OrdinalIgnoreCase)) return "ФСЗН";
        if (text.Contains("Подоход", StringComparison.OrdinalIgnoreCase)
            || text.Contains("прибыл", StringComparison.OrdinalIgnoreCase)
            || text.Contains("НДФЛ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("налог на прибыль", StringComparison.OrdinalIgnoreCase)) return "Подоходный";
        if (text.Contains("НПД", StringComparison.OrdinalIgnoreCase)
            || text.Contains("профдоход", StringComparison.OrdinalIgnoreCase)) return "Другое";
        if (text.Contains("Един", StringComparison.OrdinalIgnoreCase)) return "Другое";
        if (KnownTypes.Any(k => text.Equals(k, StringComparison.OrdinalIgnoreCase))) return text;
        return null;
    }
}

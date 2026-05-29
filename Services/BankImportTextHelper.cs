using System.Text.RegularExpressions;

namespace MiniFinance.Services;

/// <summary>Нормализация полей банковского импорта (короткие описания, без дублей).</summary>
public static class BankImportTextHelper
{
    private static readonly Dictionary<string, string> OperationShortLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Плата за перевод средств с КС на КС"] = "Комиссия за перевод",
        ["Плата за перевод"] = "Комиссия за перевод",
        ["Оплата товаров и услуг"] = "Оплата",
        ["Возвращенная покупка"] = "Возврат",
        ["Начисление Cash-back"] = "Cash-back",
        ["Пополнение наличными"] = "Пополнение",
        ["Перевод средств"] = "Перевод",
        ["Перевод P2P"] = "P2P-перевод",
        ["Выдача наличных"] = "Снятие наличных",
        ["Капитализация"] = "Капитализация",
    };

    private static readonly Regex CardDigitRx = new(@"\b(\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex GarbageMerchantRx = new(
        @"^(?:\d{2}:\d{2}\s*)+$|^(?:карты|карта|оплата|перевод|\d+\s*)+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string SimplifyOperationType(string? operationType)
    {
        if (string.IsNullOrWhiteSpace(operationType))
            return "Операция";
        var t = operationType.Trim();
        if (OperationShortLabels.TryGetValue(t, out var shortLabel))
            return shortLabel;
        return t.Length > 40 ? t[..40] + "…" : t;
    }

    public static string? NormalizeCardNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "-")
            return null;

        var compact = NormalizeSpaces(raw);
        var digits = CardDigitRx.Matches(compact).Select(m => m.Groups[1].Value).ToList();
        var hasMask = compact.Contains('*', StringComparison.Ordinal);

        if (digits.Count == 0 && !hasMask)
            return null;

        var last4 = digits.Count > 0 ? digits[^1] : null;
        if (hasMask && last4 != null)
            return $"•••• {last4}";
        if (last4 != null)
            return last4.Length == 4 ? $"•••• {last4}" : last4;

        return null;
    }

    public static bool IsPlausibleCardField(string? card)
    {
        if (string.IsNullOrWhiteSpace(card) || card == "-")
            return false;
        return card.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5
               && card.Length <= 28;
    }

    public static bool IsGarbageMerchant(string? merchant)
    {
        if (string.IsNullOrWhiteSpace(merchant))
            return true;
        if (merchant.Length < 3)
            return true;
        if (GarbageMerchantRx.IsMatch(merchant.Trim()))
            return true;
        if (merchant.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12)
            return true;
        return false;
    }

    public static string BuildImportDescription(string operationType, string? merchant)
    {
        var label = SimplifyOperationType(operationType);
        merchant = SanitizeMerchantForDisplay(merchant);
        if (string.IsNullOrWhiteSpace(merchant) || IsGarbageMerchant(merchant))
            return label;
        return $"{label} — {Truncate(merchant, 72)}";
    }

    public static string? SanitizeMerchantForDisplay(string? raw) =>
        BankPdfStatementParser.SanitizeMerchant(raw);

    public static string BuildImportNotes(string? card, DateTime? posted, decimal? exchangeRate, bool isFee, int? linkedIndex)
    {
        var notes = new List<string>();
        var normalizedCard = NormalizeCardNumber(card);
        if (!string.IsNullOrEmpty(normalizedCard))
            notes.Add($"Карта {normalizedCard}");
        if (posted.HasValue)
            notes.Add($"Учёт: {posted:dd.MM.yyyy}");
        if (exchangeRate.HasValue)
            notes.Add($"Курс {exchangeRate:N4}");
        if (isFee && linkedIndex.HasValue)
            notes.Add("Комиссия к переводу");
        return string.Join(" · ", notes);
    }

    public static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max].TrimEnd() + "…";

    private static string NormalizeSpaces(string s) =>
        Regex.Replace(s.Trim(), @"\s+", " ");
}

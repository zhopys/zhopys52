namespace MiniFinance.Services;

/// <summary>Официальный графический знак белорусского рубля (шрифт NBRB, U+E901).</summary>
public static class BynCurrency
{
    public const char Symbol = '\uE901';
    public const string SymbolString = "\uE901";
    /// <summary>Суффикс для PDF/Excel и строк с встроенным шрифтом NBRB.</summary>
    public const string Suffix = " \uE901";
    /// <summary>Текстовый суффикс для диалогов, title и прочего plain text без шрифта NBRB.</summary>
    public const string PlainSuffix = " BYN";
}

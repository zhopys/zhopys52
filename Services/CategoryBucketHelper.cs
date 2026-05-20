namespace MiniFinance.Services;

public static class CategoryBucketHelper
{
    private static readonly string[] CapexKeywords = ["оборуд", "capex", "капитал", "внедрен", "лиценз", "сервер"];
    private static readonly string[] PayrollKeywords = ["зарплат", "фот", "payroll", "оклад", "преми"];
    private static readonly string[] RentKeywords = ["аренд", "rent", "офис"];
    private static readonly string[] TaxKeywords = ["налог", "ндс", "усн", "фсзн", "tax"];

    public static bool IsCapex(string category) => Match(category, CapexKeywords);
    public static bool IsPayroll(string category) => Match(category, PayrollKeywords);
    public static bool IsRent(string category) => Match(category, RentKeywords);
    public static bool IsTax(string category) => Match(category, TaxKeywords);

    public static bool IsOpex(string category) => !IsCapex(category);

    private static bool Match(string category, string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(category)) return false;
        var c = category.ToLowerInvariant();
        return keywords.Any(k => c.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public static decimal SumExpenses(IEnumerable<(string Category, decimal Amount)> items, Func<string, bool> predicate) =>
        items.Where(x => x.Amount < 0 && predicate(x.Category))
            .Sum(x => Math.Abs(x.Amount));
}

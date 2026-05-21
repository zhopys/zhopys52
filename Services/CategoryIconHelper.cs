using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public static class CategoryIconHelper
{
    public static readonly IReadOnlyList<(string Key, string Label, string BiClass)> PickerIcons =
    [
        ("receipt", "Чек", "bi-receipt"),
        ("building", "Здание", "bi-building"),
        ("users", "Люди", "bi-people"),
        ("megaphone", "Реклама", "bi-megaphone"),
        ("server", "Сервер", "bi-hdd-stack"),
        ("package", "Коробка", "bi-box"),
        ("briefcase", "Портфель", "bi-briefcase"),
        ("wifi", "Связь", "bi-wifi"),
        ("cart", "Покупки", "bi-cart"),
        ("cash", "Деньги", "bi-cash-stack"),
        ("graph-up", "Доход", "bi-graph-up-arrow"),
        ("graph-down", "Расход", "bi-graph-down-arrow"),
        ("shield", "Страховка", "bi-shield-check"),
        ("tools", "Ремонт", "bi-tools"),
        ("fuel", "Топливо", "bi-fuel-pump"),
        ("more", "Прочее", "bi-three-dots")
    ];

    private static readonly Dictionary<string, string> IconToBi = PickerIcons
        .ToDictionary(x => x.Key, x => x.BiClass, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, (string Icon, string Color)> NameDefaults =
        CategoryDefaults.All.ToDictionary(
            x => x.Name,
            x => (x.Icon, x.Color),
            StringComparer.OrdinalIgnoreCase);

    public static string ResolveBiClass(string? icon, string? categoryName = null)
    {
        if (!string.IsNullOrWhiteSpace(icon))
        {
            var key = icon.Trim().ToLowerInvariant();
            if (IconToBi.TryGetValue(key, out var bi))
                return bi;
            if (key.StartsWith("bi-"))
                return key;
        }

        if (!string.IsNullOrWhiteSpace(categoryName) &&
            NameDefaults.TryGetValue(categoryName, out var d))
            return ResolveBiClass(d.Icon);

        return "bi-tag";
    }

    public static string ResolveColor(string? color, string? categoryName, CategoryType type)
    {
        if (!string.IsNullOrWhiteSpace(color))
            return color.Trim();

        if (!string.IsNullOrWhiteSpace(categoryName) &&
            NameDefaults.TryGetValue(categoryName, out var d) &&
            !string.IsNullOrWhiteSpace(d.Color))
            return d.Color;

        return type == CategoryType.Income ? "#22c55e" : "#64748b";
    }

    public static (string Icon, string Color) Resolve(Category? cat)
    {
        if (cat == null)
            return ("tag", "#64748b");

        var icon = string.IsNullOrWhiteSpace(cat.Icon) && NameDefaults.TryGetValue(cat.Name, out var d)
            ? d.Icon
            : cat.Icon ?? "tag";

        var color = ResolveColor(cat.Color, cat.Name, cat.Type);
        return (icon, color);
    }

    public static IEnumerable<string> SplitKeywords(string? keywords) =>
        string.IsNullOrWhiteSpace(keywords)
            ? Enumerable.Empty<string>()
            : keywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

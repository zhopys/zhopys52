using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;
using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public class CategorizationService : ICategorizationService
    {
        private readonly ApplicationDbContext _db;

        private static readonly Dictionary<string, List<string>> FallbackKeywords = new()
        {
            ["Аренда"] = new() { "аренда", "rent", "арендная", "помещение" },
            ["Зарплата"] = new() { "зарплата", "salary", "оклад", "зп", "персонал" },
            ["Налоги"] = new() { "налог", "tax", "ндфл", "ндс", "взнос", "фсс" },
            ["Связь"] = new() { "интернет", "телефон", "связь", "мобильная", "internet", "phone" },
            ["Офисные расходы"] = new() { "канцелярия", "бумага", "принтер", "офис", "мебель", "office" },
            ["Реклама"] = new() { "реклама", "маркетинг", "продвижение", "advertising", "google ads" },
            ["Хостинг"] = new() { "хостинг", "hosting", "сервер", "домен", "cloud" },
            ["Закупки"] = new() { "закупка", "закупки", "поставка", "товар", "материал" },
            ["Доход от услуг"] = new() { "оплата", "продажа", "payment", "sale", "клиент", "услуга", "service", "консультация" }
        };

        public CategorizationService(ApplicationDbContext db)
        {
            _db = db;
        }

        public string CategorizeTransaction(string description, decimal amount)
        {
            if (string.IsNullOrWhiteSpace(description))
                return amount >= 0 ? CategoryDefaults.DefaultIncome : CategoryDefaults.DefaultExpense;

            var lowerDescription = description.ToLowerInvariant();
            var categories = _db.Categories.AsNoTracking().ToList();

            foreach (var cat in categories.Where(c => !string.IsNullOrWhiteSpace(c.Keywords)))
            {
                var keywords = cat.Keywords!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (keywords.Any(k => lowerDescription.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return cat.Name;
            }

            foreach (var pair in FallbackKeywords)
            {
                if (pair.Value.Any(k => lowerDescription.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return pair.Key;
            }

            return amount >= 0 ? CategoryDefaults.DefaultIncome : CategoryDefaults.DefaultExpense;
        }

        public async Task EnsureDefaultCategoriesAsync()
        {
            foreach (var (name, type, keywords, icon, color) in CategoryDefaults.All)
            {
                if (await _db.Categories.AnyAsync(c => c.Name == name))
                    continue;

                _db.Categories.Add(new Category
                {
                    Name = name,
                    Type = type,
                    Keywords = string.IsNullOrWhiteSpace(keywords) ? null : keywords,
                    Icon = icon,
                    Color = color,
                    IsDefault = true
                });
            }

            await _db.SaveChangesAsync();
        }

        public async Task<List<string>> GetCategoryNamesAsync() =>
            await _db.Categories.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
    }
}

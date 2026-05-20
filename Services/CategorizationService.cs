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
                var existing = await _db.Categories.FirstOrDefaultAsync(c => c.Name == name);
                if (existing != null) continue;

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

        public async Task<Category> EnsureCategoryAsync(string name, decimal amount)
        {
            var trimmed = (name ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed))
                trimmed = amount >= 0 ? CategoryDefaults.DefaultIncome : CategoryDefaults.DefaultExpense;

            var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Name == trimmed);
            if (cat != null) return cat;

            cat = new Category
            {
                Name = trimmed,
                Type = amount >= 0 ? CategoryType.Income : CategoryType.Expense,
                IsDefault = false
            };
            _db.Categories.Add(cat);
            await _db.SaveChangesAsync();
            return cat;
        }

        public async Task<List<string>> GetCategoryNamesAsync() =>
            await _db.Categories.Where(c => !c.IsHidden).OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();

        public async Task<Category> UpdateCategoryAsync(int id, string name, CategoryType type, string? keywords)
        {
            var cat = await _db.Categories.FindAsync(id)
                ?? throw new KeyNotFoundException("Категория не найдена");

            var trimmed = name.Trim();
            if (string.IsNullOrEmpty(trimmed))
                throw new InvalidOperationException("Название категории обязательно");

            if (await _db.Categories.AnyAsync(c => c.Id != id && c.Name.ToLower() == trimmed.ToLower()))
                throw new InvalidOperationException("Категория с таким названием уже существует");

            var oldName = cat.Name;
            if (!string.Equals(oldName, trimmed, StringComparison.Ordinal))
            {
                var txs = await _db.Transactions.Where(t => t.Category == oldName).ToListAsync();
                foreach (var t in txs)
                    t.Category = trimmed;
            }

            cat.Name = trimmed;
            cat.Type = type;
            cat.Keywords = string.IsNullOrWhiteSpace(keywords) ? null : keywords.Trim();
            await _db.SaveChangesAsync();
            return cat;
        }
    }
}

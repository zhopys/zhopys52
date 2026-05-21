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
                if (existing != null)
                {
                    if (string.IsNullOrWhiteSpace(existing.Icon))
                        existing.Icon = icon;
                    if (string.IsNullOrWhiteSpace(existing.Color))
                        existing.Color = color;
                    if (string.IsNullOrWhiteSpace(existing.Keywords) && !string.IsNullOrWhiteSpace(keywords))
                        existing.Keywords = keywords;
                    continue;
                }

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

        public async Task<Category?> GetCategoryAsync(int id) =>
            await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);

        public async Task<CategoryStatsDto> GetCategoryStatsAsync(int categoryId, string userId)
        {
            var cat = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId)
                ?? throw new KeyNotFoundException("Категория не найдена");

            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var txs = await _db.Transactions.AsNoTracking()
                .Where(t => t.UserId == userId && t.Category == cat.Name)
                .Select(t => new { t.Amount, t.Date })
                .ToListAsync();

            var monthTxs = txs.Where(t => t.Date >= monthStart).ToList();

            return new CategoryStatsDto
            {
                Id = categoryId,
                TransactionCount = txs.Count,
                TotalAmount = txs.Sum(t => t.Amount),
                MonthTransactionCount = monthTxs.Count,
                MonthAmount = monthTxs.Sum(t => t.Amount)
            };
        }

        public async Task<Category> UpdateCategoryAsync(CategoryUpdateRequest request)
        {
            var cat = await _db.Categories.FindAsync(request.Id)
                ?? throw new KeyNotFoundException("Категория не найдена");

            var trimmed = request.Name.Trim();
            if (string.IsNullOrEmpty(trimmed))
                throw new InvalidOperationException("Название категории обязательно");

            if (await _db.Categories.AnyAsync(c => c.Id != request.Id && c.Name.ToLower() == trimmed.ToLower()))
                throw new InvalidOperationException("Категория с таким названием уже существует");

            var oldName = cat.Name;
            if (!string.Equals(oldName, trimmed, StringComparison.Ordinal))
            {
                var txs = await _db.Transactions.Where(t => t.Category == oldName).ToListAsync();
                foreach (var t in txs)
                    t.Category = trimmed;
            }

            cat.Name = trimmed;
            cat.Type = request.Type;
            cat.Keywords = string.IsNullOrWhiteSpace(request.Keywords) ? null : request.Keywords.Trim();
            cat.Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
            cat.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
            cat.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            cat.MonthlyBudget = request.MonthlyBudget is > 0 ? request.MonthlyBudget : null;

            await _db.SaveChangesAsync();
            return cat;
        }

        public async Task SetCategoryHiddenAsync(int id, bool hidden)
        {
            var cat = await _db.Categories.FindAsync(id)
                ?? throw new KeyNotFoundException("Категория не найдена");
            cat.IsHidden = hidden;
            await _db.SaveChangesAsync();
        }
    }
}

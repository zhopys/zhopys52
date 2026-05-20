using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public static class CategoryDefaults
    {
        public static readonly (string Name, CategoryType Type, string Keywords, string Icon, string Color)[] All =
        {
            ("Налоги", CategoryType.Expense, "налог,tax,ндфл,ндс,взнос,фсс,усн", "receipt", "#ef4444"),
            ("Аренда", CategoryType.Expense, "аренда,rent,арендная,помещение", "building", "#f97316"),
            ("Зарплата", CategoryType.Expense, "зарплата,salary,оклад,зп,персонал,фот", "users", "#8b5cf6"),
            ("Реклама", CategoryType.Expense, "реклама,маркетинг,продвижение,advertising,яндекс директ", "megaphone", "#ec4899"),
            ("Хостинг", CategoryType.Expense, "хостинг,hosting,сервер,домен,cloud,aws", "server", "#06b6d4"),
            ("Закупки", CategoryType.Expense, "закупка,закупки,поставка,товар,материал", "package", "#14b8a6"),
            ("Офисные расходы", CategoryType.Expense, "канцелярия,офис,office,мебель,оборудование,бумага", "briefcase", "#64748b"),
            ("Связь", CategoryType.Expense, "интернет,телефон,связь,мобильная,internet,phone,подписка", "wifi", "#3b82f6"),
            ("Прочее", CategoryType.Expense, "", "more", "#94a3b8"),
            ("Доход от услуг", CategoryType.Income, "оплата,продажа,payment,sale,клиент,услуга,service,консультация", "trending-up", "#22c55e")
        };

        public static string DefaultExpense => "Прочее";
        public static string DefaultIncome => "Доход от услуг";
    }
}

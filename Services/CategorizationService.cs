namespace MiniFinance.Services
{
    public class CategorizationService : ICategorizationService
    {
        private readonly Dictionary<string, List<string>> _categoryKeywords = new()
        {
            // Расходы
            ["Аренда"] = new() { "аренда", "rent", "арендная", "помещение" },
            ["Зарплата"] = new() { "зарплата", "salary", "оклад", "зп", "выплата сотрудникам", "персонал" },
            ["Налоги"] = new() { "налог", "tax", "ндфл", "ндс", "взнос", "пенсионный фонд", "фсс" },
            ["Коммунальные"] = new() { "электричество", "вода", "газ", "отопление", "коммунальные", "utility", "свет" },
            ["Связь"] = new() { "интернет", "телефон", "связь", "мобильная", "internet", "phone", "сотовая" },
            ["Офис"] = new() { "канцелярия", "бумага", "принтер", "офис", "мебель", "оборудование", "office" },
            ["Реклама"] = new() { "реклама", "маркетинг", "продвижение", "advertising", "яндекс директ", "google ads" },
            ["Транспорт"] = new() { "бензин", "топливо", "такси", "транспорт", "доставка", "логистика", "fuel", "газ" },
            ["Закупка товаров"] = new() { "закупка", "товар", "поставщик", "материалы", "сырье", "purchase", "supplier" },
            ["Банк"] = new() { "комиссия", "банк", "обслуживание счета", "bank", "fee", "процент" },
            ["Страхование"] = new() { "страхование", "страховка", "insurance", "полис" },
            ["Обучение"] = new() { "обучение", "курсы", "тренинг", "семинар", "training", "education" },
            ["Программное обеспечение"] = new() { "подписка", "saas", "софт", "лицензия", "software", "subscription", "облако" },

            // Доходы
            ["Продажи"] = new() { "оплата", "продажа", "payment", "sale", "клиент", "заказ", "invoice" },
            ["Услуги"] = new() { "услуга", "service", "консультация", "работа" },
            ["Инвестиции"] = new() { "дивиденды", "проценты", "инвестиции", "investment", "dividend" }
        };

        public string CategorizeTransaction(string description, decimal amount)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return amount >= 0 ? "Доход" : "Расход";
            }

            var lowerDescription = description.ToLower();

            // Ищем совпадения по ключевым словам
            foreach (var category in _categoryKeywords)
            {
                if (category.Value.Any(keyword => lowerDescription.Contains(keyword)))
                {
                    return category.Key;
                }
            }

            // Если не нашли совпадений, используем базовую категоризацию
            return amount >= 0 ? "Доход" : "Расход";
        }
    }
}

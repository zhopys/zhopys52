using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public static class TaxSystemInfo
{
    public static string GetLabel(TaxSystem? system) => system switch
    {
        TaxSystem.USN => "УСН (упрощённая)",
        TaxSystem.OSN => "ОСН (общая)",
        TaxSystem.NPD => "НПД (профдоход)",
        TaxSystem.UnifiedTax => "Единый налог",
        _ => "не указана"
    };

    public static string GetShortLabel(TaxSystem system) => system switch
    {
        TaxSystem.USN => "УСН",
        TaxSystem.OSN => "ОСН",
        TaxSystem.NPD => "НПД",
        TaxSystem.UnifiedTax => "Единый",
        _ => system.ToString()
    };

    public static string GetDescription(TaxSystem system) => system switch
    {
        TaxSystem.USN =>
            "Упрощённая система (в основном для юридических лиц): налог считается от выручки за квартал. " +
            "Типовая ставка — около 6% от дохода; расходы на размер налога не влияют. " +
            "Нужны данные о выручке и квартальная декларация. Актуальные ставки — на портале МНС РБ.",
        TaxSystem.OSN =>
            "Общая система: документально подтверждённые доходы минус расходы. " +
            "Для ИП — подоходный налог (ориентир 16%), для юрлиц — налог на прибыль (ориентир 20%). " +
            "Могут применяться НДС и иные платежи — уточняйте по виду деятельности.",
        TaxSystem.NPD =>
            "Налог на профессиональный доход: для самозанятых и ИП без наёмных работников. " +
            "Ставка 4% с доходов от физических лиц и 8% — от юридических лиц и ИП. " +
            "В учёте ведётся приложение «Профдоход»; здесь — оценка по сводке выручки.",
        TaxSystem.UnifiedTax =>
            "Единый налог: фиксированная сумма по виду деятельности и населённому пункту (такси, аренда жилья, торговля и др.). " +
            "Размер задаётся Налоговым кодексом РБ — введите сумму из уведомления/справочника.",
        _ => ""
    };

    public static string GetMnsHint() =>
        "Официальные ставки, сроки и формы — на портале МНС РБ и в системе «Электронное декларирование». " +
        "MiniFinance даёт оценку по учётным данным, не заменяет декларацию.";

    public static bool UsesExpenses(TaxSystem? system) =>
        system is TaxSystem.OSN;

    public static bool UsesRevenueOnly(TaxSystem? system) =>
        system is TaxSystem.USN;

    public static bool UsesNpdSplit(TaxSystem? system) =>
        system is TaxSystem.NPD;

    public static bool UsesUnifiedFixed(TaxSystem? system) =>
        system is TaxSystem.UnifiedTax;
}

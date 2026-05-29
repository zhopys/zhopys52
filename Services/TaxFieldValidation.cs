using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public static class TaxFieldValidation
{
    public const int MaxTaxNameLength = 200;
    public const int MaxBillNameLength = 200;
    public const int MaxRuleNameLength = 120;
    public const int MaxFormulaLength = 500;
    public const int MaxReceiptLength = 500;
    public const int MaxCompanyNameLength = 200;
    public const int MaxUnpLength = 20;
    public const decimal MaxAmount = 999_999_999.99m;

    public sealed class FieldErrors
    {
        private readonly Dictionary<string, string> _items = new(StringComparer.OrdinalIgnoreCase);

        public bool HasErrors => _items.Count > 0;

        public void Set(string field, string error) => _items[field] = error;

        public void Clear() => _items.Clear();

        public string? Get(string field) => _items.TryGetValue(field, out var error) ? error : null;

        public string? FirstError() => _items.Values.FirstOrDefault();
    }

    public static FieldErrors ValidateTaxPayment(string typeSelect, string customName, decimal amount, DateTime dueDate, string? fullNameOverride = null)
    {
        var errors = new FieldErrors();
        if (!string.IsNullOrWhiteSpace(fullNameOverride))
        {
            if (fullNameOverride.Trim().Length > MaxTaxNameLength)
                errors.Set("customName", $"Название не длиннее {MaxTaxNameLength} символов");
        }
        else
            ValidatePaymentName(typeSelect, customName, errors);

        ValidateAmount(amount, errors, "amount");
        ValidateDueDate(dueDate, errors, "dueDate");
        return errors;
    }

    public static FieldErrors ValidatePartialPayment(decimal amount, decimal remaining, string? receipt)
    {
        var errors = new FieldErrors();
        if (amount <= 0)
            errors.Set("amount", "Укажите сумму больше нуля");
        else if (amount > remaining)
            errors.Set("amount", $"Сумма не может превышать остаток ({remaining:N2} BYN)");
        else if (amount > MaxAmount)
            errors.Set("amount", $"Сумма не может быть больше {MaxAmount:N0} BYN");

        if (!string.IsNullOrWhiteSpace(receipt) && receipt.Trim().Length > MaxReceiptLength)
            errors.Set("receipt", $"Примечание не длиннее {MaxReceiptLength} символов");

        return errors;
    }

    public static FieldErrors ValidateBill(string name, decimal amount, DateTime dueDate)
    {
        var errors = new FieldErrors();
        if (string.IsNullOrWhiteSpace(name))
            errors.Set("name", "Укажите название счёта");
        else if (name.Trim().Length > MaxBillNameLength)
            errors.Set("name", $"Название не длиннее {MaxBillNameLength} символов");

        ValidateAmount(amount, errors, "amount");
        ValidateDueDate(dueDate, errors, "dueDate", allowPast: true);
        return errors;
    }

    public static FieldErrors ValidateAutoRule(TaxAutoRule rule)
    {
        var errors = new FieldErrors();
        if (string.IsNullOrWhiteSpace(rule.Name))
            errors.Set("name", "Укажите название правила");
        else if (rule.Name.Trim().Length > MaxRuleNameLength)
            errors.Set("name", $"Название не длиннее {MaxRuleNameLength} символов");

        if (string.IsNullOrWhiteSpace(rule.PaymentName))
            errors.Set("paymentName", "Укажите вид платежа");
        else if (rule.PaymentName.Trim().Length > MaxTaxNameLength)
            errors.Set("paymentName", $"Вид платежа не длиннее {MaxTaxNameLength} символов");

        if (string.IsNullOrWhiteSpace(rule.Formula))
            errors.Set("formula", "Укажите формулу");
        else if (rule.Formula.Trim().Length > MaxFormulaLength)
            errors.Set("formula", $"Формула не длиннее {MaxFormulaLength} символов");
        else
        {
            var test = TaxFormulaEvaluator.TryEvaluate(rule.Formula, new TaxFormulaContext { Income = 1000, Expenses = 200 });
            if (!test.Ok)
                errors.Set("formula", test.Error ?? "Неверная формула");
        }

        if (rule.DueDayOfMonth < 1 || rule.DueDayOfMonth > 28)
            errors.Set("dueDay", "День срока — от 1 до 28");

        if (rule.DueMonthOffset < 0 || rule.DueMonthOffset > 3)
            errors.Set("dueOffset", "Смещение месяца — от 0 до 3");

        return errors;
    }

    public static (bool Ok, string? Error) ValidateCalculatorInput(TaxCalculatorInput input, TaxSystem? system)
    {
        if (!system.HasValue)
            return (false, "Укажите систему налогообложения в настройках организации");

        if (input.Income < 0 || input.Expenses < 0
            || input.IncomeFromIndividuals < 0 || input.IncomeFromLegalEntities < 0
            || input.UnifiedTaxAmount < 0)
            return (false, "Суммы не могут быть отрицательными");

        if (input.Income > MaxAmount || input.Expenses > MaxAmount
            || input.IncomeFromIndividuals > MaxAmount || input.IncomeFromLegalEntities > MaxAmount
            || input.UnifiedTaxAmount > MaxAmount)
            return (false, $"Сумма не может превышать {MaxAmount:N0} BYN");

        return system.Value switch
        {
            TaxSystem.UnifiedTax when input.UnifiedTaxAmount <= 0 =>
                (false, "Укажите сумму единого налога за период"),
            TaxSystem.NPD when input.IncomeFromIndividuals <= 0 && input.IncomeFromLegalEntities <= 0 && input.Income <= 0 =>
                (false, "Укажите доход от физлиц и/или юрлиц"),
            TaxSystem.OSN or TaxSystem.USN when input.Income <= 0 && input.IncomeFromIndividuals <= 0 && input.IncomeFromLegalEntities <= 0 =>
                (false, "Укажите выручку / доход за период"),
            _ => (true, null)
        };
    }

    public static FieldErrors ValidateOrganizationSettings(string companyName, string unp, decimal minCashBalance)
    {
        var errors = new FieldErrors();
        if (!string.IsNullOrWhiteSpace(companyName) && companyName.Trim().Length > MaxCompanyNameLength)
            errors.Set("companyName", $"Название не длиннее {MaxCompanyNameLength} символов");

        var unpCheck = ValidateUnp(unp);
        if (!unpCheck.Ok)
            errors.Set("unp", unpCheck.Error!);

        if (minCashBalance < 0)
            errors.Set("minCashBalance", "Минимальный остаток не может быть отрицательным");
        else if (minCashBalance > MaxAmount)
            errors.Set("minCashBalance", $"Слишком большое значение (макс. {MaxAmount:N0})");

        return errors;
    }

    public static (bool Ok, string? Error) ValidateUnp(string? unp)
    {
        if (string.IsNullOrWhiteSpace(unp))
            return (true, null);

        var value = unp.Trim();
        if (value.Length > MaxUnpLength)
            return (false, $"УНП не длиннее {MaxUnpLength} символов");
        if (!value.All(char.IsDigit))
            return (false, "УНП должен содержать только цифры");
        if (value.Length != 9)
            return (false, "УНП должен содержать 9 цифр");

        return (true, null);
    }

    public static string TranslateError(Exception ex) => ex switch
    {
        ArgumentException arg => string.IsNullOrWhiteSpace(arg.Message) ? "Некорректные данные" : arg.Message,
        InvalidOperationException op => string.IsNullOrWhiteSpace(op.Message) ? "Операция недоступна" : op.Message,
        UnauthorizedAccessException => AccessDeniedMessages.ForPolicy(AuthorizationPolicies.CanManageTaxes),
        _ => "Не удалось выполнить операцию. Проверьте данные и попробуйте снова."
    };

    private static void ValidatePaymentName(string typeSelect, string customName, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(typeSelect))
        {
            errors.Set("type", "Выберите вид налога");
            return;
        }

        if (typeSelect == "Другое")
        {
            if (string.IsNullOrWhiteSpace(customName))
                errors.Set("customName", "Укажите название платежа");
            else if (customName.Trim().Length > MaxTaxNameLength)
                errors.Set("customName", $"Название не длиннее {MaxTaxNameLength} символов");
        }
    }

    private static void ValidateAmount(decimal amount, FieldErrors errors, string field)
    {
        if (amount <= 0)
            errors.Set(field, "Укажите сумму больше нуля");
        else if (amount > MaxAmount)
            errors.Set(field, $"Сумма не может быть больше {MaxAmount:N0} BYN");
    }

    private static void ValidateDueDate(DateTime dueDate, FieldErrors errors, string field, bool allowPast = false)
    {
        if (dueDate.Year < 2000 || dueDate.Year > 2100)
            errors.Set(field, "Укажите корректную дату срока уплаты");
        else if (!allowPast && dueDate.Date < DateTime.Today.AddYears(-1))
            errors.Set(field, "Срок уплаты слишком далеко в прошлом");
    }
}

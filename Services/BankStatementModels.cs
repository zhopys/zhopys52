using MiniFinance.Data.Models;

namespace MiniFinance.Services;

/// <summary>Метаданные банковской выписки (заголовок).</summary>
public sealed class AccountStatementHeader
{
    public string? RegistrationNumber { get; set; }
    public string? Phone { get; set; }
    public DateTime? GeneratedAt { get; set; }
    public string? OwnerFullName { get; set; }
    public string? AccountName { get; set; }
    public string? Iban { get; set; }
    public string Currency { get; set; } = "BYN";
    public decimal? OverdraftLimit { get; set; }
    public decimal? OpeningBalance { get; set; }
    public decimal? ClosingBalance { get; set; }
    public decimal? TotalCredits { get; set; }
    public decimal? TotalDebits { get; set; }
    public DateTime? LastOperationDate { get; set; }
}

/// <summary>Строка операции из PDF до импорта в БД.</summary>
public sealed class ParsedBankTransaction
{
    public int LineIndex { get; set; }
    public string? CardNumber { get; set; }
    public DateTime OperationDateTime { get; set; }
    public DateTime? PostedDateTime { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string? MerchantPlace { get; set; }
    public string OperationCurrency { get; set; } = "BYN";
    public bool IsIncome { get; set; }
    public decimal OperationAmount { get; set; }
    public decimal AccountAmount { get; set; }
    public decimal? BalanceAfter { get; set; }
    public int? Mcc { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool IsFee { get; set; }
    public int? LinkedTransactionIndex { get; set; }
    public decimal? ExchangeRate { get; set; }

    public string BuildDescription()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(OperationType))
            parts.Add(OperationType.Trim());
        if (!string.IsNullOrWhiteSpace(MerchantPlace))
            parts.Add(MerchantPlace.Trim());
        if (OperationCurrency != "BYN" && OperationAmount > 0)
            parts.Add($"{OperationAmount:N2} {OperationCurrency}");
        if (Mcc.HasValue)
            parts.Add($"MCC {Mcc}");
        return string.Join(" · ", parts);
    }

    public string BuildNotes()
    {
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(CardNumber) && CardNumber != "-")
            notes.Add($"Карта: {CardNumber}");
        if (PostedDateTime.HasValue)
            notes.Add($"Отражено: {PostedDateTime:dd.MM.yyyy HH:mm}");
        if (ExchangeRate.HasValue)
            notes.Add($"Курс: {ExchangeRate:N4}");
        if (IsFee && LinkedTransactionIndex.HasValue)
            notes.Add($"Комиссия к операции #{LinkedTransactionIndex}");
        return string.Join("; ", notes);
    }
}

public sealed class AccountStatement
{
    public AccountStatementHeader Header { get; set; } = new();
    public List<ParsedBankTransaction> Transactions { get; set; } = new();
}

public sealed class BankStatementImportResult
{
    public AccountStatement Statement { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
    public List<CsvImportError> Errors { get; set; } = new();
    public int SkippedDuplicates { get; set; }
    public int SkippedInvalid { get; set; }
    public int TotalLinesExtracted { get; set; }
}

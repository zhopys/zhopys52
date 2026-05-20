using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MiniFinance.Data.Models;
using UglyToad.PdfPig;

namespace MiniFinance.Services;

public class BankPdfStatementParser : IBankPdfStatementParser
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly string[] KnownOperationTypes =
    [
        "Плата за перевод средств с КС на КС",
        "Плата за перевод",
        "Оплата товаров и услуг",
        "Возвращенная покупка",
        "Начисление Cash-back",
        "Пополнение наличными",
        "Перевод средств",
        "Перевод P2P",
        "Выдача наличных",
        "Капитализация",
    ];

    private static readonly Regex DateTimeRx = new(
        @"(?<d>\d{2}\.\d{2}\.\d{4})\s+(?<t>\d{2}:\d{2})",
        RegexOptions.Compiled);

    private static readonly Regex TransactionTailRx = new(
        @"(?<cur>BYN|USD|EUR|RUB)\s+(?<dir>приход|расход)\s+(?<opAmt>[\d\s]+[,.]\d{2})\s+(?<accAmt>[\d\s]+[,.]\d{2})(?:\s+(?<bal>[\d\s]+[,.]\d{2}))?(?:\s+(?<mcc>\d{4,6}))?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CardPrefixRx = new(
        @"^(?<card>[\d\s*\-]+|-)\s+",
        RegexOptions.Compiled);

    private readonly ICategorizationService _categorization;
    private readonly ILogger<BankPdfStatementParser> _logger;

    public BankPdfStatementParser(ICategorizationService categorization, ILogger<BankPdfStatementParser> logger)
    {
        _categorization = categorization;
        _logger = logger;
    }

    public async Task<BankStatementImportResult> ParseAsync(
        Stream pdfStream,
        string userId,
        ISet<string>? existingHashes = null,
        CancellationToken cancellationToken = default)
    {
        var result = new BankStatementImportResult();
        var seenHashes = existingHashes != null
            ? new HashSet<string>(existingHashes, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var ms = new MemoryStream();
        await pdfStream.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        var lines = ExtractLinesFromPdf(bytes);
        result.TotalLinesExtracted = lines.Count;
        result.Statement.Header = ParseHeader(lines);
        result.Statement.Transactions = ParseTransactions(lines, result.Errors);

        LinkFeesToTransfers(result.Statement.Transactions);
        await _categorization.EnsureDefaultCategoriesAsync();

        foreach (var pt in result.Statement.Transactions)
        {
            var signed = pt.IsIncome ? Math.Abs(pt.AccountAmount) : -Math.Abs(pt.AccountAmount);
            var desc = pt.BuildDescription();
            pt.Category = MccCategoryMapper.ResolveCategory(
                pt.Mcc, pt.OperationType, pt.MerchantPlace, pt.IsIncome, _categorization, desc, signed);

            var hash = TransactionHash.Compute(pt.OperationDateTime.Date, signed, desc);
            if (!seenHashes.Add(hash))
            {
                result.SkippedDuplicates++;
                continue;
            }

            var counterparty = ExtractCounterpartyName(pt.MerchantPlace, pt.OperationType);
            result.Transactions.Add(new Transaction
            {
                Date = pt.OperationDateTime.Date,
                Amount = signed,
                Description = desc.Length > 200 ? desc[..200] : desc,
                Category = pt.Category,
                UserId = userId,
                IsConfirmed = true,
                PaymentMethod = pt.CardNumber is "-" or null ? null : PaymentMethod.Card,
                Counterparty = counterparty,
                Notes = Truncate(pt.BuildNotes(), 1000),
                CreatedAt = DateTime.UtcNow
            });
        }

        return result;
    }

    internal static List<string> ExtractLinesFromPdf(byte[] pdfBytes)
    {
        using var doc = PdfDocument.Open(pdfBytes);
        var raw = new List<(double Y, double X, string Text)>();

        foreach (var page in doc.GetPages())
        {
            foreach (var word in page.GetWords())
            {
                var t = word.Text.Trim();
                if (t.Length == 0) continue;
                raw.Add((word.BoundingBox.Bottom, word.BoundingBox.Left, t));
            }
        }

        if (raw.Count == 0)
            return [];

        const double rowTolerance = 4.0;
        var sorted = raw.OrderByDescending(x => x.Y).ToList();
        var rows = new List<List<(double Y, double X, string Text)>>();
        var current = new List<(double Y, double X, string Text)> { sorted[0] };
        var anchorY = sorted[0].Y;

        for (var i = 1; i < sorted.Count; i++)
        {
            var item = sorted[i];
            if (Math.Abs(item.Y - anchorY) <= rowTolerance)
                current.Add(item);
            else
            {
                rows.Add(current);
                current = [item];
                anchorY = item.Y;
            }
        }
        rows.Add(current);

        var lines = new List<string>();
        foreach (var row in rows)
        {
            var line = string.Join(" ", row.OrderBy(w => w.X).Select(w => w.Text));
            line = NormalizeSpaces(line);
            if (ShouldSkipLine(line)) continue;
            lines.Add(line);
        }

        return lines;
    }

    internal static AccountStatementHeader ParseHeader(IReadOnlyList<string> lines)
    {
        var h = new AccountStatementHeader();
        var text = string.Join("\n", lines);

        h.RegistrationNumber = MatchGroup(text, @"Регистрационный номер приложения:\s*(\d+)");
        h.Phone = MatchGroup(text, @"Номер телефона:\s*([\d+]+)");
        h.GeneratedAt = ParseDateTime(MatchGroup(text, @"Дата формирования:\s*(.+?)(?:\n|$)"));
        h.OwnerFullName = MatchGroup(text, @"ФИО владельца:\s*(.+?)(?:\n|$)");
        h.AccountName = MatchGroup(text, @"Название счета:\s*(.+?)(?:\n|$)");
        h.Iban = MatchGroup(text, @"IBAN:\s*(BY[\dA-Z]+)");
        h.Currency = MatchGroup(text, @"Валюта счета:\s*(\w+)") ?? "BYN";
        h.OverdraftLimit = ParseDecimal(MatchGroup(text, @"Лимит овердрафта:\s*([\d\s,.]+)"));
        h.OpeningBalance = ParseDecimal(MatchGroup(text, @"Остаток на начало периода:\s*([\d\s,.]+)"));
        h.ClosingBalance = ParseDecimal(MatchGroup(text, @"Остаток на конец периода:\s*([\d\s,.]+)"));
        h.TotalCredits = ParseDecimal(MatchGroup(text, @"Зачислено за период:\s*([\d\s,.]+)"));
        h.TotalDebits = ParseDecimal(MatchGroup(text, @"Сумма расходных операций за период:\s*([\d\s,.]+)"));
        h.LastOperationDate = ParseDateOnly(MatchGroup(text, @"Дата последней операции:\s*(\d{2}\.\d{2}\.\d{4})"));

        return h;
    }

    internal List<ParsedBankTransaction> ParseTransactions(IReadOnlyList<string> lines, List<CsvImportError> errors)
    {
        var list = new List<ParsedBankTransaction>();
        var inTable = false;
        var lineIndex = 0;
        ParsedBankTransaction? pendingContinuation = null;

        foreach (var line in lines)
        {
            lineIndex++;

            if (line.Contains("Номер карты", StringComparison.OrdinalIgnoreCase)
                && line.Contains("Дата", StringComparison.OrdinalIgnoreCase))
            {
                inTable = true;
                continue;
            }

            if (!inTable)
            {
                if (LooksLikeTransactionLine(line))
                    inTable = true;
                else
                    continue;
            }

            if (IsTableHeaderRepeat(line))
                continue;

            if (TryParseTransactionLine(line, lineIndex, out var tx))
            {
                if (pendingContinuation != null)
                    list.Add(pendingContinuation);
                pendingContinuation = tx;
                continue;
            }

            if (pendingContinuation != null && !LooksLikeTransactionLine(line))
            {
                pendingContinuation.MerchantPlace = JoinMerchant(pendingContinuation.MerchantPlace, line);
                continue;
            }

            if (LooksLikeTransactionLine(line))
            {
                errors.Add(new CsvImportError
                {
                    LineNumber = lineIndex,
                    Message = "Не удалось разобрать строку операции.",
                    RawLine = line
                });
            }
        }

        if (pendingContinuation != null)
            list.Add(pendingContinuation);

        return list;
    }

    internal static bool TryParseTransactionLine(string line, int lineIndex, out ParsedBankTransaction tx)
    {
        tx = new ParsedBankTransaction { LineIndex = lineIndex };

        if (line.Contains("00.00.0000", StringComparison.Ordinal))
            return false;

        var work = line;
        string? card = null;
        var cardMatch = CardPrefixRx.Match(work);
        if (cardMatch.Success)
        {
            card = cardMatch.Groups["card"].Value.Trim();
            work = work[cardMatch.Length..].Trim();
        }

        var dates = DateTimeRx.Matches(work).ToList();
        if (dates.Count == 0)
            return false;

        if (!TryParseDateTimeMatch(dates[0], out var opDt))
            return false;

        DateTime? posted = null;
        var afterDates = work;
        if (dates.Count >= 2)
        {
            TryParseDateTimeMatch(dates[1], out var postedDt);
            posted = postedDt;
            afterDates = work[(dates[1].Index + dates[1].Length)..].Trim();
        }
        else
        {
            afterDates = work[(dates[0].Index + dates[0].Length)..].Trim();
        }

        var tailMatch = TransactionTailRx.Match(afterDates);
        if (!tailMatch.Success)
            return false;

        var middle = afterDates[..tailMatch.Index].Trim();
        var (opType, merchant) = SplitOperationTypeAndMerchant(middle);

        tx.CardNumber = card;
        tx.OperationDateTime = opDt;
        tx.PostedDateTime = posted;
        tx.OperationType = opType;
        tx.MerchantPlace = merchant;
        tx.OperationCurrency = tailMatch.Groups["cur"].Value.ToUpperInvariant();
        tx.IsIncome = tailMatch.Groups["dir"].Value.Equals("приход", StringComparison.OrdinalIgnoreCase);
        tx.OperationAmount = ParseDecimal(tailMatch.Groups["opAmt"].Value) ?? 0;
        tx.AccountAmount = ParseDecimal(tailMatch.Groups["accAmt"].Value) ?? tx.OperationAmount;
        tx.BalanceAfter = ParseDecimal(tailMatch.Groups["bal"].Value);
        if (tailMatch.Groups["mcc"].Success && int.TryParse(tailMatch.Groups["mcc"].Value, out var mcc))
            tx.Mcc = mcc;

        if (tx.OperationCurrency != "BYN" && tx.OperationAmount > 0 && tx.AccountAmount > 0)
            tx.ExchangeRate = Math.Round(tx.AccountAmount / tx.OperationAmount, 4);

        tx.IsFee = MccCategoryMapper.IsFeeOperationType(opType);
        return true;
    }

    internal static void LinkFeesToTransfers(List<ParsedBankTransaction> transactions)
    {
        for (var i = 0; i < transactions.Count; i++)
        {
            var t = transactions[i];
            if (!t.IsFee) continue;

            for (var j = i - 1; j >= 0 && j >= i - 3; j--)
            {
                var prev = transactions[j];
                if (prev.IsFee) continue;
                if (prev.OperationType.Contains("перевод", StringComparison.OrdinalIgnoreCase)
                    || prev.OperationType.Contains("P2P", StringComparison.OrdinalIgnoreCase))
                {
                    t.LinkedTransactionIndex = prev.LineIndex;
                    break;
                }
            }
        }
    }

    private static (string OperationType, string? Merchant) SplitOperationTypeAndMerchant(string middle)
    {
        foreach (var known in KnownOperationTypes.OrderByDescending(s => s.Length))
        {
            if (!middle.StartsWith(known, StringComparison.OrdinalIgnoreCase))
                continue;

            var merchant = middle.Length > known.Length
                ? middle[known.Length..].Trim()
                : null;
            return (known, string.IsNullOrWhiteSpace(merchant) ? null : merchant);
        }

        var firstSpace = middle.IndexOf(' ');
        if (firstSpace > 0)
            return (middle[..firstSpace].Trim(), middle[(firstSpace + 1)..].Trim());

        return (middle, null);
    }

    private static bool LooksLikeTransactionLine(string line) =>
        DateTimeRx.IsMatch(line) && TransactionTailRx.IsMatch(line);

    private static bool IsTableHeaderRepeat(string line) =>
        line.Contains("Номер карты", StringComparison.OrdinalIgnoreCase)
        || line.Contains("МСС код", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Приход/Расход", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldSkipLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        if (line.All(c => c is '.' or ' ' or '\t')) return true;
        if (line.Length < 4) return true;
        return false;
    }

    private static string NormalizeSpaces(string s) =>
        Regex.Replace(s, @"\s+", " ").Trim();

    private static string? MatchGroup(string text, string pattern)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static bool TryParseDateTimeMatch(Match m, out DateTime dt)
    {
        var s = $"{m.Groups["d"].Value} {m.Groups["t"].Value}";
        return DateTime.TryParseExact(s, ["dd.MM.yyyy HH:mm", "dd.MM.yyyy H:mm"],
            RuCulture, DateTimeStyles.None, out dt);
    }

    private static DateTime? ParseDateTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParseExact(s.Trim(), ["dd.MM.yyyy HH:mm", "dd.MM.yyyy H:mm"],
                RuCulture, DateTimeStyles.None, out var dt))
            return dt;
        return null;
    }

    private static DateTime? ParseDateOnly(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParseExact(s.Trim(), "dd.MM.yyyy", RuCulture, DateTimeStyles.None, out var d))
            return d;
        return null;
    }

    private static decimal? ParseDecimal(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var n = s.Replace(" ", "").Replace(",", ".");
        return decimal.TryParse(n, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string? ExtractCounterpartyName(string? merchant, string operationType)
    {
        if (!string.IsNullOrWhiteSpace(merchant))
        {
            var m = merchant.Trim();
            var cut = m.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(cut) && cut.Length <= 150)
                return cut;
        }

        if (operationType.Contains("перевод", StringComparison.OrdinalIgnoreCase))
            return "Перевод";

        return null;
    }

    private static string JoinMerchant(string? existing, string line)
    {
        var combined = string.IsNullOrWhiteSpace(existing) ? line : $"{existing} {line}";
        return combined.Length > 300 ? combined[..300] : combined;
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];
}

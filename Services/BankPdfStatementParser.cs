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
        @"(?:(?<cur>BYN|USD|EUR|RUB)\s+)?(?<dir>приход|расход|поступление|списание)\s+(?<opAmt>[\d\s]+[,.]\d{2})\s+(?<accAmt>[\d\s]+[,.]\d{2})(?:\s+(?<bal>[\d\s]+[,.]\d{2}))?(?:\s+(?<mcc>\d{4,6}))?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TransactionTailAnywhereRx = new(
        @"(?:(?<cur>BYN|USD|EUR|RUB)\s+)?(?<dir>приход|расход|поступление|списание)\s+(?<opAmt>[\d\s]+[,.]\d{2})\s+(?<accAmt>[\d\s]+[,.]\d{2})(?:\s+(?<bal>[\d\s]+[,.]\d{2}))?(?:\s+(?<mcc>\d{4,6}))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CardPrefixRx = new(
        @"^(?<card>[\d\s*\-]+|-)\s+",
        RegexOptions.Compiled);

    private static readonly Regex TimeOnlyRx = new(
        @"^\d{2}:\d{2}(\s+\d{2}:\d{2})*\s*$",
        RegexOptions.Compiled);

    private static readonly Regex MerchantSlugRx = new(
        @"[A-ZА-ЯЁ0-9][A-ZА-ЯЁ0-9a-zа-яё0-9\-]*(?:/\s*[A-ZА-ЯЁA-Z0-9][\w\-]*)?",
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
        result.Statement.Transactions = ParseTransactionsStatic(lines, result.Errors);

        LinkFeesToTransfers(result.Statement.Transactions);
        await _categorization.EnsureDefaultCategoriesAsync();

        foreach (var pt in result.Statement.Transactions)
        {
            if (pt.AccountAmount <= 0)
            {
                result.SkippedInvalid++;
                continue;
            }

            var signed = pt.IsIncome ? Math.Abs(pt.AccountAmount) : -Math.Abs(pt.AccountAmount);
            if (Math.Abs(signed) < 0.01m)
            {
                result.SkippedInvalid++;
                continue;
            }

            var desc = pt.BuildDescription();
            pt.Category = MccCategoryMapper.ResolveCategory(
                pt.Mcc, pt.OperationType, pt.MerchantPlace, pt.IsIncome, _categorization, desc, signed);

            var hash = TransactionHash.Compute(
                pt.OperationDateTime.Date,
                signed,
                $"{BankImportTextHelper.SimplifyOperationType(pt.OperationType)}|{pt.MerchantPlace}|{pt.Mcc}");
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
        var raw = new List<WordPosition>();

        foreach (var page in doc.GetPages())
        {
            foreach (var word in page.GetWords())
            {
                var t = word.Text.Trim();
                if (t.Length == 0) continue;
                var box = word.BoundingBox;
                raw.Add(new WordPosition(box.Bottom, box.Left, box.Right, box.Top - box.Bottom, t));
            }
        }

        if (raw.Count == 0)
            return [];

        var heights = raw.Select(w => w.Height).Where(h => h > 0.5).OrderBy(h => h).ToList();
        var medianHeight = heights.Count > 0 ? heights[heights.Count / 2] : 10.0;
        var rowTolerance = Math.Clamp(medianHeight * 0.42, 2.0, 6.0);

        var sorted = raw.OrderByDescending(x => x.Y).ThenBy(x => x.X).ToList();
        var rows = new List<List<WordPosition>>();
        var current = new List<WordPosition> { sorted[0] };
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

        var columnGap = Math.Max(18.0, medianHeight * 1.8);
        var lines = new List<string>();
        foreach (var row in rows.OrderByDescending(r => r[0].Y))
        {
            var line = JoinRowWords(row.OrderBy(w => w.X).ToList(), columnGap);
            line = NormalizeSpaces(line);
            if (ShouldSkipLine(line)) continue;
            lines.Add(line);
        }

        return lines;
    }

    private static string JoinRowWords(IReadOnlyList<WordPosition> words, double columnGap)
    {
        if (words.Count == 0) return "";
        var sb = new StringBuilder(words[0].Text);
        for (var i = 1; i < words.Count; i++)
        {
            var gap = words[i].X - words[i - 1].Right;
            sb.Append(gap > columnGap ? " \t " : ' ');
            sb.Append(words[i].Text);
        }
        return sb.ToString();
    }

    private readonly struct WordPosition(double y, double x, double right, double height, string text)
    {
        public double Y { get; } = y;
        public double X { get; } = x;
        public double Right { get; } = right;
        public double Height { get; } = height;
        public string Text { get; } = text;
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
        if (string.IsNullOrEmpty(h.Iban))
        {
            var iban = Regex.Match(text, @"\b(BY\d{2}[A-Z0-9]{24,32})\b", RegexOptions.IgnoreCase);
            if (iban.Success)
                h.Iban = iban.Groups[1].Value.ToUpperInvariant();
        }
        h.Currency = MatchGroup(text, @"Валюта счета:\s*(\w+)") ?? "BYN";
        h.OverdraftLimit = ParseDecimal(MatchGroup(text, @"Лимит овердрафта:\s*([\d\s,.]+)"));
        h.OpeningBalance = ParseDecimal(MatchGroup(text, @"Остаток на начало периода:\s*([\d\s,.]+)"));
        h.ClosingBalance = ParseDecimal(MatchGroup(text, @"Остаток на конец периода:\s*([\d\s,.]+)"));
        h.TotalCredits = ParseDecimal(MatchGroup(text, @"Зачислено за период:\s*([\d\s,.]+)"))
            ?? ParseDecimal(MatchGroup(text, @"Зачислено[^\d]{0,40}([\d\s,.]+)"));
        h.TotalDebits = ParseDecimal(MatchGroup(text, @"Сумма расходных операций за период:\s*([\d\s,.]+)"))
            ?? ParseDecimal(MatchGroup(text, @"расходных операций[^\d]{0,40}([\d\s,.]+)"));
        h.LastOperationDate = ParseDateOnly(MatchGroup(text, @"Дата последней операции:\s*(\d{2}\.\d{2}\.\d{4})"));

        return h;
    }

    internal static List<ParsedBankTransaction> ParseTransactionsStatic(IReadOnlyList<string> lines, List<CsvImportError> errors)
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

            var parsed = ParseTransactionsFromLine(line, lineIndex);
            if (parsed.Count > 0)
            {
                if (pendingContinuation != null)
                    list.Add(pendingContinuation);
                list.AddRange(parsed);
                pendingContinuation = null;
                continue;
            }

            if (pendingContinuation != null && IsMerchantContinuation(line))
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
                    RawLine = Truncate(line, 200)
                });
            }
        }

        if (pendingContinuation != null && pendingContinuation.AccountAmount > 0)
            list.Add(pendingContinuation);

        return DeduplicateParsedTransactions(list);
    }

    internal static List<ParsedBankTransaction> ParseTransactionsFromLine(string line, int lineIndex)
    {
        var tails = TransactionTailAnywhereRx.Matches(line).ToList();
        if (tails.Count == 0)
            return [];

        if (tails.Count == 1)
        {
            return TryParseTransactionLine(line, lineIndex, out var single) && IsValidParsedTransaction(single)
                ? [single]
                : [];
        }

        var list = new List<ParsedBankTransaction>();
        string? sharedCard = null;
        var cardMatch = CardPrefixRx.Match(line);
        if (cardMatch.Success)
        {
            var rawCard = cardMatch.Groups["card"].Value.Trim();
            if (BankImportTextHelper.IsPlausibleCardField(rawCard))
                sharedCard = BankImportTextHelper.NormalizeCardNumber(rawCard);
        }

        for (var i = 0; i < tails.Count; i++)
        {
            var segmentStart = i == 0 ? 0 : tails[i - 1].Index + tails[i - 1].Length;
            var segment = line[segmentStart..(tails[i].Index + tails[i].Length)].Trim();
            if (!TryParseTransactionSegment(segment, lineIndex, sharedCard, out var tx))
                continue;
            if (!IsValidParsedTransaction(tx))
                continue;
            list.Add(tx);
        }

        return list;
    }

    internal static bool TryParseTransactionLine(string line, int lineIndex, out ParsedBankTransaction tx) =>
        TryParseTransactionSegment(line, lineIndex, null, out tx);

    private static bool TryParseTransactionSegment(
        string segment,
        int lineIndex,
        string? inheritedCard,
        out ParsedBankTransaction tx)
    {
        tx = new ParsedBankTransaction { LineIndex = lineIndex };

        if (segment.Contains("00.00.0000", StringComparison.Ordinal))
            return false;

        var work = segment;
        string? card = inheritedCard;
        var cardMatch = CardPrefixRx.Match(work);
        if (cardMatch.Success)
        {
            var rawCard = cardMatch.Groups["card"].Value.Trim();
            if (BankImportTextHelper.IsPlausibleCardField(rawCard))
                card = BankImportTextHelper.NormalizeCardNumber(rawCard) ?? rawCard;
            work = work[cardMatch.Length..].Trim();
        }
        else if (!string.IsNullOrWhiteSpace(inheritedCard))
        {
            card = inheritedCard;
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
            tailMatch = TransactionTailAnywhereRx.Match(afterDates);
        if (!tailMatch.Success)
            return false;

        var middle = afterDates[..tailMatch.Index].Trim();
        var (opType, merchant) = SplitOperationTypeAndMerchant(middle);
        merchant = SanitizeMerchant(merchant);

        tx.CardNumber = BankImportTextHelper.NormalizeCardNumber(card);
        tx.OperationDateTime = opDt;
        tx.PostedDateTime = posted;
        tx.OperationType = opType;
        tx.MerchantPlace = merchant;
        var dir = tailMatch.Groups["dir"].Value;
        tx.OperationCurrency = tailMatch.Groups["cur"].Success
            ? tailMatch.Groups["cur"].Value.ToUpperInvariant()
            : "BYN";
        tx.IsIncome = dir.Equals("приход", StringComparison.OrdinalIgnoreCase)
            || dir.Equals("поступление", StringComparison.OrdinalIgnoreCase);
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

    private static bool IsValidParsedTransaction(ParsedBankTransaction tx) =>
        tx.AccountAmount > 0 && tx.OperationDateTime.Year >= 2000;

    private static List<ParsedBankTransaction> DeduplicateParsedTransactions(List<ParsedBankTransaction> source)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ParsedBankTransaction>();
        foreach (var t in source)
        {
            var key = $"{t.OperationDateTime:yyyyMMddHHmm}|{t.AccountAmount:F2}|{t.IsIncome}|{SanitizeMerchant(t.MerchantPlace)}|{t.OperationType}";
            if (!seen.Add(key))
                continue;
            result.Add(t);
        }
        return result;
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
        var work = StripNoiseFromMiddle(middle);
        if (string.IsNullOrWhiteSpace(work))
            return ("", null);

        foreach (var known in KnownOperationTypes.OrderByDescending(s => s.Length))
        {
            if (!work.StartsWith(known, StringComparison.OrdinalIgnoreCase))
                continue;

            var merchant = work.Length > known.Length
                ? work[known.Length..].Trim()
                : null;
            return (known, SanitizeMerchant(merchant));
        }

        foreach (var known in KnownOperationTypes.OrderByDescending(s => s.Length))
        {
            var idx = work.IndexOf(known, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var merchant = work[(idx + known.Length)..].Trim();
            return (known, SanitizeMerchant(merchant));
        }

        var firstSpace = work.IndexOf(' ');
        if (firstSpace > 0)
            return (work[..firstSpace].Trim(), SanitizeMerchant(work[(firstSpace + 1)..].Trim()));

        return (work, null);
    }

    private static string StripNoiseFromMiddle(string middle)
    {
        var s = middle.Trim();
        s = Regex.Replace(s, @"\b\d{2}:\d{2}\b", " ");
        s = Regex.Replace(s, @"\b(карты|карта|на\s+КС)\b", " ", RegexOptions.IgnoreCase);
        return NormalizeSpaces(s);
    }

    internal static string? SanitizeMerchant(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();
        s = Regex.Replace(s, @"\b\d{2}\.\d{2}\.\d{4}\b", " ");
        s = Regex.Replace(s, @"\b\d{2}:\d{2}\b", " ");
        s = Regex.Replace(s, @"\b(карты|карта|на\s+КС|КС)\b", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(BLR|BYN|USD|EUR|RUB|BY)\b", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(MINSKIY)\b", " ", RegexOptions.IgnoreCase);

        foreach (var known in KnownOperationTypes.OrderByDescending(x => x.Length))
            s = Regex.Replace(s, $@"({Regex.Escape(known)}\s*)+", "$1 ", RegexOptions.IgnoreCase);

        s = NormalizeSpaces(s);
        if (string.IsNullOrWhiteSpace(s) || s.Length < 2)
            return null;
        if (TimeOnlyRx.IsMatch(s))
            return null;
        if (s.All(c => char.IsDigit(c) || c is ' ' or '.' or ':' or '/'))
            return null;

        return s;
    }

    private static bool IsMerchantContinuation(string line)
    {
        if (LooksLikeTransactionLine(line))
            return false;
        if (TransactionTailAnywhereRx.IsMatch(line))
            return false;
        if (TimeOnlyRx.IsMatch(line.Trim()))
            return false;
        if (DateTimeRx.Matches(line).Count >= 2)
            return false;
        if (line.Contains("Приход", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Расход", StringComparison.OrdinalIgnoreCase))
            return false;

        return line.Any(char.IsLetter);
    }

    private static bool LooksLikeTransactionLine(string line) =>
        DateTimeRx.IsMatch(line)
        && (TransactionTailRx.IsMatch(line) || TransactionTailAnywhereRx.IsMatch(line));

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
        merchant = SanitizeMerchant(merchant);
        if (!string.IsNullOrWhiteSpace(merchant))
        {
            var slug = MerchantSlugRx.Matches(merchant)
                .Select(m => m.Value.Trim())
                .Where(v => v.Length >= 3 && !v.Equals("BY", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.Length)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(slug))
                return CounterpartyNameMatcher.CanonicalDisplayName(slug);

            var cut = merchant.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(cut))
                return CounterpartyNameMatcher.CanonicalDisplayName(cut);
        }

        if (operationType.Contains("перевод", StringComparison.OrdinalIgnoreCase))
            return "Перевод";

        return null;
    }

    private static string JoinMerchant(string? existing, string line)
    {
        var extra = SanitizeMerchant(line) ?? line.Trim();
        if (string.IsNullOrWhiteSpace(extra))
            return existing ?? "";
        var combined = string.IsNullOrWhiteSpace(existing) ? extra : $"{existing} {extra}";
        combined = SanitizeMerchant(combined) ?? combined;
        return combined.Length > 300 ? combined[..300] : combined;
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];
}

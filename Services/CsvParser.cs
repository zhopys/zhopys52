using System.Globalization;
using System.Text;
using MiniFinance.Data.Models;
using Microsoft.Extensions.Logging;

namespace MiniFinance.Services
{
    public interface ICsvParser
    {
        Task<CsvImportResult> ParseAsync(Stream fileStream, string userId, ISet<string>? existingHashes = null, CsvColumnMapping? mapping = null);
    }

    public class CsvParser : ICsvParser
    {
        private readonly ICategorizationService _categorizationService;
        private readonly ILogger<CsvParser> _logger;

        private static readonly string[] DateHeaders = { "date", "дата", "transactiondate", "дататранзакции" };
        private static readonly string[] AmountHeaders = { "amount", "сумма", "sum", "value", "сум" };
        private static readonly string[] DescriptionHeaders = { "description", "описание", "details", "назначение", "memo" };
        private static readonly string[] CategoryHeaders = { "category", "категория", "cat" };

        public CsvParser(ICategorizationService categorizationService, ILogger<CsvParser> logger)
        {
            _categorizationService = categorizationService;
            _logger = logger;
        }

        public async Task<CsvImportResult> ParseAsync(Stream fileStream, string userId, ISet<string>? existingHashes = null, CsvColumnMapping? mapping = null)
        {
            var result = new CsvImportResult { Mapping = mapping ?? new CsvColumnMapping() };
            var seenHashes = existingHashes != null
                ? new HashSet<string>(existingHashes, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

            var lines = new List<string>();
            while (await reader.ReadLineAsync() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    lines.Add(line);
            }

            if (lines.Count == 0)
            {
                result.Errors.Add(new CsvImportError { LineNumber = 0, Message = "Файл пуст." });
                return result;
            }

            result.DetectedDelimiter = DetectDelimiter(lines[0]);
            result.TotalLines = lines.Count;

            var startIndex = 0;
            if (result.Mapping.HasHeader || LooksLikeHeader(lines[0]))
            {
                ApplyHeaderMapping(lines[0], result.DetectedDelimiter, result.Mapping);
                startIndex = 1;
                result.Mapping.HasHeader = true;
            }

            for (var i = startIndex; i < lines.Count; i++)
            {
                var lineNumber = i + 1;
                var line = lines[i];

                try
                {
                    var fields = ParseCsvLine(line, result.DetectedDelimiter);
                    if (fields.Length < 3)
                    {
                        result.SkippedInvalid++;
                        result.Errors.Add(new CsvImportError
                        {
                            LineNumber = lineNumber,
                            Message = "Недостаточно полей (нужны дата, сумма, описание).",
                            RawLine = line
                        });
                        continue;
                    }

                    var dateStr = GetField(fields, result.Mapping.DateIndex);
                    var amountStr = GetField(fields, result.Mapping.AmountIndex);
                    var description = GetField(fields, result.Mapping.DescriptionIndex);
                    var categoryField = result.Mapping.CategoryIndex >= 0 && result.Mapping.CategoryIndex < fields.Length
                        ? GetField(fields, result.Mapping.CategoryIndex)
                        : string.Empty;

                    if (string.IsNullOrWhiteSpace(dateStr) || string.IsNullOrWhiteSpace(amountStr))
                    {
                        result.SkippedInvalid++;
                        result.Errors.Add(new CsvImportError
                        {
                            LineNumber = lineNumber,
                            Message = "Отсутствуют дата или сумма.",
                            RawLine = line
                        });
                        continue;
                    }

                    if (!TryParseDate(dateStr, out var date))
                    {
                        result.SkippedInvalid++;
                        result.Errors.Add(new CsvImportError
                        {
                            LineNumber = lineNumber,
                            Message = $"Некорректная дата: {dateStr}",
                            RawLine = line
                        });
                        continue;
                    }

                    if (!TryParseAmount(amountStr, out var amount))
                    {
                        result.SkippedInvalid++;
                        result.Errors.Add(new CsvImportError
                        {
                            LineNumber = lineNumber,
                            Message = $"Некорректная сумма: {amountStr}",
                            RawLine = line
                        });
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(description))
                    {
                        result.SkippedInvalid++;
                        result.Errors.Add(new CsvImportError
                        {
                            LineNumber = lineNumber,
                            Message = "Описание не может быть пустым.",
                            RawLine = line
                        });
                        continue;
                    }

                    if (amount == 0)
                    {
                        result.SkippedInvalid++;
                        result.Errors.Add(new CsvImportError
                        {
                            LineNumber = lineNumber,
                            Message = "Сумма не может быть нулевой.",
                            RawLine = line
                        });
                        continue;
                    }

                    if (date > DateTime.Today.AddDays(1))
                    {
                        result.SkippedInvalid++;
                        result.Errors.Add(new CsvImportError
                        {
                            LineNumber = lineNumber,
                            Message = $"Дата {date:dd.MM.yyyy} в будущем.",
                            RawLine = line
                        });
                        continue;
                    }

                    var hash = TransactionHash.Compute(date, amount, description);
                    if (!seenHashes.Add(hash))
                    {
                        result.SkippedDuplicates++;
                        continue;
                    }

                    var category = !string.IsNullOrWhiteSpace(categoryField)
                        ? categoryField.Trim()
                        : _categorizationService.CategorizeTransaction(description, amount);

                    result.Transactions.Add(new Transaction
                    {
                        Date = date,
                        Amount = amount,
                        Description = description.Trim(),
                        Category = category,
                        UserId = userId,
                        IsConfirmed = true
                    });
                }
                catch (Exception ex)
                {
                    result.SkippedInvalid++;
                    _logger.LogWarning(ex, "CSV: ошибка в строке {LineNumber}", lineNumber);
                    result.Errors.Add(new CsvImportError
                    {
                        LineNumber = lineNumber,
                        Message = ex.Message,
                        RawLine = line
                    });
                }
            }

            return result;
        }

        private static char DetectDelimiter(string line)
        {
            var counts = new Dictionary<char, int>
            {
                [';'] = CountOutsideQuotes(line, ';'),
                [','] = CountOutsideQuotes(line, ','),
                ['\t'] = CountOutsideQuotes(line, '\t')
            };
            return counts.OrderByDescending(kv => kv.Value).First().Key;
        }

        private static int CountOutsideQuotes(string line, char delimiter)
        {
            var count = 0;
            var inQuotes = false;
            foreach (var ch in line)
            {
                if (ch == '"') inQuotes = !inQuotes;
                else if (ch == delimiter && !inQuotes) count++;
            }
            return count;
        }

        private static bool LooksLikeHeader(string line)
        {
            var lower = line.ToLowerInvariant();
            return DateHeaders.Any(h => lower.Contains(h)) &&
                   (AmountHeaders.Any(h => lower.Contains(h)) || lower.Contains("amount") || lower.Contains("сумм"));
        }

        private static void ApplyHeaderMapping(string headerLine, char delimiter, CsvColumnMapping mapping)
        {
            var headers = ParseCsvLine(headerLine, delimiter)
                .Select(h => h.Trim().ToLowerInvariant())
                .ToArray();

            mapping.DateIndex = FindIndex(headers, DateHeaders);
            mapping.AmountIndex = FindIndex(headers, AmountHeaders);
            mapping.DescriptionIndex = FindIndex(headers, DescriptionHeaders);
            mapping.CategoryIndex = FindIndex(headers, CategoryHeaders);

            if (mapping.DateIndex < 0) mapping.DateIndex = 0;
            if (mapping.AmountIndex < 0) mapping.AmountIndex = 1;
            if (mapping.DescriptionIndex < 0) mapping.DescriptionIndex = 2;
            if (mapping.CategoryIndex < 0) mapping.CategoryIndex = headers.Length > 3 ? 3 : -1;
        }

        private static int FindIndex(string[] headers, string[] candidates)
        {
            for (var i = 0; i < headers.Length; i++)
            {
                if (candidates.Any(c => headers[i].Contains(c, StringComparison.OrdinalIgnoreCase)))
                    return i;
            }
            return -1;
        }

        private static string GetField(string[] fields, int index) =>
            index >= 0 && index < fields.Length ? fields[index].Trim() : string.Empty;

        private static bool TryParseDate(string value, out DateTime date)
        {
            var formats = new[]
            {
                "yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy", "MM/dd/yyyy",
                "yyyy/MM/dd", "dd-MM-yyyy"
            };
            return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                   || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                   || DateTime.TryParse(value, new CultureInfo("ru-RU"), DateTimeStyles.None, out date);
        }

        private static bool TryParseAmount(string value, out decimal amount)
        {
            var normalized = value.Trim().Replace(" ", "").Replace("Br", "", StringComparison.OrdinalIgnoreCase);
            if (normalized.Contains(',') && !normalized.Contains('.'))
                normalized = normalized.Replace(',', '.');

            return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out amount)
                   || decimal.TryParse(value, NumberStyles.Any, new CultureInfo("ru-RU"), out amount);
        }

        private static string[] ParseCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return Array.Empty<string>();

            var cur = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cur.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (ch == delimiter && !inQuotes)
                {
                    result.Add(cur.ToString());
                    cur.Clear();
                }
                else
                {
                    cur.Append(ch);
                }
            }

            result.Add(cur.ToString());
            return result.ToArray();
        }
    }
}

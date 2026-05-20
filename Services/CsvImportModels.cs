using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public class CsvColumnMapping
    {
        public int DateIndex { get; set; } = 0;
        public int AmountIndex { get; set; } = 1;
        public int DescriptionIndex { get; set; } = 2;
        public int CategoryIndex { get; set; } = 3;
        public bool HasHeader { get; set; } = true;
    }

    public class CsvImportError
    {
        public int LineNumber { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? RawLine { get; set; }
    }

    public class CsvImportResult
    {
        public List<Transaction> Transactions { get; set; } = new();
        public List<CsvImportError> Errors { get; set; } = new();
        public int TotalLines { get; set; }
        public int SkippedDuplicates { get; set; }
        public int SkippedInvalid { get; set; }
        public char DetectedDelimiter { get; set; } = ';';
        public CsvColumnMapping Mapping { get; set; } = new();
    }

    public static class TransactionHash
    {
        public static string Compute(DateTime date, decimal amount, string description) =>
            $"{date:yyyy-MM-dd}|{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{description.Trim().ToLowerInvariant()}";
    }
}

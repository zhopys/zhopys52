namespace MiniFinance.Data.Models;

public class TransactionImportBatch
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string SourceType { get; set; } = "csv";
    public string? FileName { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public bool IsRolledBack { get; set; }
    public DateTime? RolledBackAt { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}

public sealed class ImportBatchMetadata
{
    public string SourceType { get; init; } = "csv";
    public string? FileName { get; init; }
}

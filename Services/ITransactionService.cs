using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public class TransactionListFilter
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int? ProjectId { get; set; }
        public string? Category { get; set; }
        public string? Search { get; set; }
        public string? Type { get; set; } // all, income, expense
    }

    public class TransactionImportResult
    {
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int? ImportBatchId { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public interface ITransactionService
    {
        Task<Transaction> CreateAsync(Transaction transaction, string userId);
        Task<Transaction> UpdateAsync(Transaction transaction, string userId);
        Task DeleteAsync(int id, string userId);
        Task<Transaction?> GetAsync(int id, string userId);
        Task<List<Transaction>> ListAsync(string userId, TransactionListFilter? filter = null);
        Task<TransactionImportResult> ImportManyAsync(IEnumerable<Transaction> transactions, string userId, ImportBatchMetadata? batchMeta = null);
        Task<int> RollbackImportAsync(int batchId, string userId);
        Task<TransactionImportBatch?> GetImportBatchAsync(int batchId, string userId);
        Task<TransactionImportBatch?> GetLatestImportBatchAsync(string userId);
        Task<HashSet<string>> GetExistingHashesAsync(string userId);
        Task<Transaction> UpdateCategoryAsync(int id, string category, string userId);
        Task ApproveAsync(int id, string userId);
        Task RejectAsync(int id, string userId);
        Task<List<Transaction>> ListPendingApprovalAsync(string userId);
    }
}

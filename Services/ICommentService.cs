using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public interface ICommentService
{
    Task<List<TransactionComment>> ListAsync(int transactionId, string userId);
    Task<TransactionComment> AddAsync(int transactionId, string userId, string text, string? authorName);
}

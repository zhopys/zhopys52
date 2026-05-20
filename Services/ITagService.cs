using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public interface ITagService
{
    Task<List<Tag>> ListAsync(string userId);
    Task<Tag> CreateAsync(string userId, string name, string? color = null);
    Task SetTransactionTagsAsync(int transactionId, string userId, IEnumerable<string> tagNames);
    Task<IReadOnlyList<string>> GetTransactionTagNamesAsync(int transactionId, string userId);
}

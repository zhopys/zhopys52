using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public interface IAttachmentService
{
    Task<TransactionAttachment> UploadAsync(int transactionId, string userId, Stream fileStream, string fileName, string contentType);
    Task<List<TransactionAttachment>> ListAsync(int transactionId, string userId);
    Task DeleteAsync(int attachmentId, string userId);
    string GetPublicUrl(TransactionAttachment attachment);
}

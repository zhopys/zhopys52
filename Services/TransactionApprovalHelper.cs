using MiniFinance.Data.Models;

namespace MiniFinance.Services;

public static class TransactionApprovalHelper
{
    public static void ApplyStatus(Transaction transaction, TransactionApprovalStatus status)
    {
        transaction.ApprovalStatus = status;
        transaction.IsConfirmed = status == TransactionApprovalStatus.Approved;
    }

    public static void Normalize(Transaction transaction)
    {
        ApplyStatus(transaction, transaction.ApprovalStatus);
    }

    public static string Label(TransactionApprovalStatus status) => status switch
    {
        TransactionApprovalStatus.Pending => "На утверждении",
        TransactionApprovalStatus.Rejected => "Отклонена",
        _ => "Утверждена"
    };

    public static string BadgeClass(TransactionApprovalStatus status) => status switch
    {
        TransactionApprovalStatus.Pending => "badge-warning",
        TransactionApprovalStatus.Rejected => "badge-danger",
        _ => "badge-success"
    };

    public static string ShortLabel(TransactionApprovalStatus status) => status switch
    {
        TransactionApprovalStatus.Pending => "Ожидает",
        TransactionApprovalStatus.Rejected => "Отклон.",
        _ => "Утверж."
    };
}

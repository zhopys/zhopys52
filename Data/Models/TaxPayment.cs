using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models
{
    public class TaxPayment
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty; // НДС, УСН, ФСЗН

        public decimal Amount { get; set; }

        public DateTime DueDate { get; set; }

        public bool IsPaid { get; set; }

        public DateTime? PaidDate { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? NotificationSentDate { get; set; }

        public decimal PaidAmount { get; set; }

        [StringLength(500)]
        public string? ReceiptNote { get; set; }

        /// <summary>Операция, на основе которой создан плановый платёж.</summary>
        public int? SourceTransactionId { get; set; }
    }
}

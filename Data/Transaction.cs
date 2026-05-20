using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiniFinance.Data.Models
{
    public enum PaymentMethod
    {
        [Display(Name = "Наличные")]
        Cash = 0,
        [Display(Name = "Безналичный")]
        BankTransfer = 1,
        [Display(Name = "Карта")]
        Card = 2,
        [Display(Name = "Электронный кошелек")]
        EWallet = 3
    }

    public class Transaction
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Укажите дату")]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Укажите сумму")]
        [Range(-1000000000, 1000000000, ErrorMessage = "Сумма вне допустимого диапазона")]
        public decimal Amount { get; set; }

        [NotMapped]
        public bool IsIncome => Amount > 0;

        [Required(ErrorMessage = "Укажите описание")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "Описание до 200 символов")]
        public string Description { get; set; } = string.Empty;

        [StringLength(50)]
        public string Category { get; set; } = string.Empty;

        /// <summary>Заполняется в сервисе при сохранении, не валидируется в форме.</summary>
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }

        public int? ProjectId { get; set; }
        public Project? Project { get; set; }

        public PaymentMethod? PaymentMethod { get; set; }

        [StringLength(150)]
        public string? Counterparty { get; set; }

        public bool IsMandatory { get; set; } = false;

        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        [Display(Name = "Подтверждена")]
        public bool IsConfirmed { get; set; } = true;

        public int? CounterpartyId { get; set; }
        public CounterpartyRecord? CounterpartyEntity { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        public TransactionApprovalStatus ApprovalStatus { get; set; } = TransactionApprovalStatus.Approved;

        public string? SubmittedByUserId { get; set; }

        public ICollection<TransactionTag> TransactionTags { get; set; } = new List<TransactionTag>();
        public ICollection<TransactionAttachment> Attachments { get; set; } = new List<TransactionAttachment>();
        public ICollection<TransactionComment> Comments { get; set; } = new List<TransactionComment>();
    }
}

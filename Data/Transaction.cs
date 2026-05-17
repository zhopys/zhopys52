using System.ComponentModel.DataAnnotations;

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

        [Required]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; } = DateTime.Today;

        [Required]
        [Range(-1000000, 1000000)]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(200)]
        public string Description { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Category { get; set; } = string.Empty;

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
    }
}

using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models
{
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

        // Внешний ключ
        public string UserId { get; set; } = string.Empty;
        
        // Навигационное свойство
        public ApplicationUser? User { get; set; }
    }
}
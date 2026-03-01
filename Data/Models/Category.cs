using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models
{
    public enum CategoryType
    {
        Expense = 0,
        Income = 1
    }

    public class Category
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        // Mark if this category was loaded as a default category
        public bool IsDefault { get; set; } = false;

        // Type of category: expense or income
        public CategoryType Type { get; set; } = CategoryType.Expense;
    }
}

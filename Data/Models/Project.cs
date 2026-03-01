using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models
{
    public class Project
    {
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsDefault { get; set; } = false;
    }
}

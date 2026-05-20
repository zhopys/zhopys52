using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models;

public class Tag
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Color { get; set; }

    public ICollection<TransactionTag> TransactionTags { get; set; } = new List<TransactionTag>();
}

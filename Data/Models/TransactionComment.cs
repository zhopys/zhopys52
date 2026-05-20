using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models;

public class TransactionComment
{
    public int Id { get; set; }

    public int TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = string.Empty;

    [StringLength(100)]
    public string? AuthorName { get; set; }

    [Required]
    [StringLength(2000)]
    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

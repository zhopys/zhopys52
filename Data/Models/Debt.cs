using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models;

public class Debt
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public DebtType Type { get; set; }

    [Required]
    [StringLength(150)]
    public string CounterpartyName { get; set; } = string.Empty;

    public int? CounterpartyId { get; set; }
    public CounterpartyRecord? Counterparty { get; set; }

    [Range(0.01, 1000000000)]
    public decimal Amount { get; set; }

    [Range(0, 1000000000)]
    public decimal PaidAmount { get; set; }

    [DataType(DataType.Date)]
    public DateTime? DueDate { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    public bool IsSettled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

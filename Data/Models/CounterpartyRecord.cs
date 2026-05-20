using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models;

public enum CounterpartyType
{
    Client = 0,
    Supplier = 1,
    Both = 2
}

public class CounterpartyRecord
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    public CounterpartyType Type { get; set; } = CounterpartyType.Both;

    [StringLength(100)]
    public string? ContactPerson { get; set; }

    [StringLength(100)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(30)]
    public string? TaxId { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    /// <summary>URL логотипа или аватара (https://…).</summary>
    [StringLength(500)]
    public string? LogoUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

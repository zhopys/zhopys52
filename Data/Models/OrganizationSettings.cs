using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models
{
    public class OrganizationSettings
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;

        public string UNP { get; set; } = string.Empty;

        public TaxSystem TaxSystem { get; set; }

        // Integration settings
        public string? ApiKey { get; set; }
        public string? IntegrationUrl { get; set; }
    }
}

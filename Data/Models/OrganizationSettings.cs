using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models
{
    public class OrganizationSettings
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        /// <summary>Legacy tenant key; kept in sync with <see cref="UserId"/> for older databases.</summary>
        [Required]
        public string OrganizationId { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;

        public string UNP { get; set; } = string.Empty;

        public TaxSystem TaxSystem { get; set; }

        /// <summary>ИП или юрлицо — для расчёта ОСН (16% / 20%).</summary>
        public TaxpayerKind TaxpayerKind { get; set; } = TaxpayerKind.LegalEntity;

        // Integration settings
        public string? ApiKey { get; set; }
        public string? IntegrationUrl { get; set; }

        public decimal MinCashBalance { get; set; } = 1000m;

        public int WeekStartsOn { get; set; } = 1;

        public int FinancialYearStartMonth { get; set; } = 1;

        [StringLength(20)]
        public string DateFormat { get; set; } = "dd.MM.yyyy";

        [StringLength(80)]
        public string TimeZoneId { get; set; } = "Europe/Minsk";
    }
}

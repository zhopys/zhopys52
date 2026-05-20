using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using MiniFinance.Data.Models;

namespace MiniFinance.Data
{
    public class ApplicationUser : IdentityUser
    {
        public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
        public ICollection<Project> Projects { get; set; } = new List<Project>();

        public string BaseCurrency { get; set; } = "BYN";

        public bool EnableNotifications { get; set; } = true;

        public int NotificationDaysBefore { get; set; } = 3;

        public DateTime? CreatedAt { get; set; }

        public int? ActiveProjectId { get; set; }

        [StringLength(100)]
        public string? Department { get; set; }

        [StringLength(100)]
        public string? FirstName { get; set; }

        [StringLength(100)]
        public string? LastName { get; set; }

        public bool NotifyTaxes { get; set; } = true;

        public bool NotifyCashGaps { get; set; } = true;

        public bool NotifyBills { get; set; } = true;
    }
}

using Microsoft.AspNetCore.Identity;
using MiniFinance.Data.Models;

namespace MiniFinance.Data
{
    public class ApplicationUser : IdentityUser
    {
        public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

        public string BaseCurrency { get; set; } = "BYN";

        public bool EnableNotifications { get; set; } = true;

        public int NotificationDaysBefore { get; set; } = 3;

        public DateTime? CreatedAt { get; set; }
    }
}

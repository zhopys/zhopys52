using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models
{
    public enum ReminderFrequency
    {
        OneTime = 0,
        Monthly = 1,
        Yearly = 2
    }

    public class Reminder
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [Range(0.01, 1000000)]
        public decimal Amount { get; set; }

        [StringLength(100)]
        public string Category { get; set; } = string.Empty;

        public ReminderFrequency Frequency { get; set; } = ReminderFrequency.OneTime;

        [DataType(DataType.Date)]
        public DateTime Date { get; set; } = DateTime.Today;

        // For one-time reminders, set IsPaid when paid. For recurring, Date is updated to next occurrence.
        public bool IsPaid { get; set; } = false;

        public DateTime? PaidDate { get; set; }

        // Link to user
        public string UserId { get; set; } = string.Empty;
    }
}

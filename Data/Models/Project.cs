using System.ComponentModel.DataAnnotations;

namespace MiniFinance.Data.Models
{
    public enum ProjectStatus
    {
        [Display(Name = "Активный")]
        Active = 0,
        [Display(Name = "Планирование")]
        Planning = 1,
        [Display(Name = "Приостановлен")]
        OnHold = 2,
        [Display(Name = "Завершен")]
        Completed = 3,
        [Display(Name = "Отменен")]
        Cancelled = 4
    }

    public enum ProjectPriority
    {
        [Display(Name = "Низкий")]
        Low = 0,
        [Display(Name = "Средний")]
        Medium = 1,
        [Display(Name = "Высокий")]
        High = 2,
        [Display(Name = "Критический")]
        Critical = 3
    }

    public class Project
    {
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsDefault { get; set; } = false;

        // Новые поля
        public ProjectStatus Status { get; set; } = ProjectStatus.Planning;
        
        [Range(0, 999999999)]
        public decimal? Budget { get; set; }
        
        [Range(0, 1000)]
        [Display(Name = "Целевой ROI (%)")]
        public decimal? TargetROI { get; set; }
        
        [DataType(DataType.Date)]
        public DateTime? StartDate { get; set; }
        
        [DataType(DataType.Date)]
        public DateTime? EndDate { get; set; }
        
        public ProjectPriority? Priority { get; set; }
        
        [StringLength(100)]
        public string? ProjectManager { get; set; }

        [StringLength(100)]
        public string? Department { get; set; }
        
        [StringLength(500)]
        [Display(Name = "Ключевые показатели (KPI)")]
        public string? KPI { get; set; }
        
        [StringLength(500)]
        public string? Risks { get; set; }
        
        [StringLength(1000)]
        public string? Notes { get; set; }
    }
}
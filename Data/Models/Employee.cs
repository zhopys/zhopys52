namespace MiniFinance.Data.Models
{
    public class Employee
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public decimal Salary { get; set; }

        public DateTime HireDate { get; set; }

        public DateTime? TerminationDate { get; set; }

        public bool IsActive { get; set; } = true;

        public string Role { get; set; } = "Employee"; // Employee, Accountant, Manager
    }
}

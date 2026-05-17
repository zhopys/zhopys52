using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MiniFinance.Data.Models;

namespace MiniFinance.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Reminder> Reminders { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<TaxPayment> TaxPayments { get; set; }
        public DbSet<OrganizationSettings> OrganizationSettings { get; set; }
        public DbSet<Employee> Employees { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Transaction>()
                .HasOne(t => t.User)
                .WithMany(u => u.Transactions)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Transaction>()
                .HasOne(t => t.Project)
                .WithMany()
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Category>(b =>
            {
                b.HasIndex(c => c.Name).IsUnique();
            });

            builder.Entity<Project>(b =>
            {
                b.HasIndex(p => p.Name).IsUnique();
                b.HasIndex(p => p.Status);
                b.HasIndex(p => p.Priority);
                b.HasIndex(p => p.ProjectManager);
            });
        }
    }
}

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
        public DbSet<TaxAutoRule> TaxAutoRules { get; set; }
        public DbSet<OrganizationSettings> OrganizationSettings { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<TransactionTag> TransactionTags { get; set; }
        public DbSet<TransactionAttachment> TransactionAttachments { get; set; }
        public DbSet<TransactionComment> TransactionComments { get; set; }
        public DbSet<CounterpartyRecord> Counterparties { get; set; }
        public DbSet<Debt> Debts { get; set; }

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

            builder.Entity<Transaction>()
                .HasOne(t => t.CounterpartyEntity)
                .WithMany()
                .HasForeignKey(t => t.CounterpartyId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Category>(b =>
            {
                b.HasIndex(c => c.Name).IsUnique();
            });

            builder.Entity<Project>(b =>
            {
                b.HasIndex(p => new { p.UserId, p.Name })
                    .IsUnique()
                    .HasFilter("[IsDefault] = 0");

                b.HasIndex(p => p.Status);
                b.HasIndex(p => p.Priority);
                b.HasIndex(p => p.ProjectManager);
                b.HasIndex(p => p.UserId);

                b.HasOne(p => p.User)
                    .WithMany(u => u.Projects)
                    .HasForeignKey(p => p.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<Tag>(b =>
            {
                b.HasIndex(t => new { t.UserId, t.Name }).IsUnique();
            });

            builder.Entity<TransactionTag>(b =>
            {
                b.HasKey(tt => new { tt.TransactionId, tt.TagId });
                b.HasOne(tt => tt.Transaction).WithMany(t => t.TransactionTags).HasForeignKey(tt => tt.TransactionId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(tt => tt.Tag).WithMany(t => t.TransactionTags).HasForeignKey(tt => tt.TagId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<TransactionAttachment>(b =>
            {
                b.HasIndex(a => a.TransactionId);
                b.HasOne(a => a.Transaction).WithMany(t => t.Attachments).HasForeignKey(a => a.TransactionId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<TransactionComment>(b =>
            {
                b.HasIndex(c => c.TransactionId);
                b.HasOne(c => c.Transaction).WithMany(t => t.Comments).HasForeignKey(c => c.TransactionId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<CounterpartyRecord>(b =>
            {
                b.HasIndex(c => new { c.UserId, c.Name }).IsUnique();
            });

            builder.Entity<Debt>(b =>
            {
                b.HasIndex(d => d.UserId);
                b.HasOne(d => d.Counterparty).WithMany().HasForeignKey(d => d.CounterpartyId).OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}

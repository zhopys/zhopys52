using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;

namespace MiniFinance.Services;

/// <summary>Удаление всех учётных записей и связанных данных (для локального тестирования).</summary>
public static class UserResetService
{
    public static async Task<int> DeleteAllUsersAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var count = await userManager.Users.CountAsync();
        if (count == 0)
            return 0;

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.TransactionComments.ExecuteDeleteAsync();
            await db.TransactionAttachments.ExecuteDeleteAsync();
            await db.TransactionTags.ExecuteDeleteAsync();
            await db.Transactions.ExecuteDeleteAsync();
            await db.Reminders.ExecuteDeleteAsync();
            await db.TaxPayments.ExecuteDeleteAsync();
            await db.TaxAutoRules.ExecuteDeleteAsync();
            await db.Debts.ExecuteDeleteAsync();
            await db.Counterparties.ExecuteDeleteAsync();
            await db.Projects.ExecuteDeleteAsync();
            await db.Tags.ExecuteDeleteAsync();
            await db.OrganizationSettings.ExecuteDeleteAsync();

            await db.Database.ExecuteSqlRawAsync("DELETE FROM AspNetUserTokens");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM AspNetUserLogins");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM AspNetUserClaims");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM AspNetUserRoles");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM AspNetUsers");

            await tx.CommitAsync();
            return count;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniFinance.Data;

namespace MiniFinance.Services;

public static class RoleSeedService
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        foreach (var legacy in AppRoles.LegacyRoleMap.Keys)
        {
            if (!await roleManager.RoleExistsAsync(legacy))
                continue;

            var usersInLegacy = await userManager.GetUsersInRoleAsync(legacy);
            foreach (var user in usersInLegacy)
            {
                var target = AppRoles.LegacyRoleMap[legacy];
                if (!await userManager.IsInRoleAsync(user, target))
                    await userManager.AddToRoleAsync(user, target);
                await userManager.RemoveFromRoleAsync(user, legacy);
            }
        }

        var standaloneUsers = await userManager.Users
            .Where(u => u.WorkspaceOwnerUserId == null)
            .ToListAsync();

        foreach (var user in standaloneUsers)
        {
            var roles = await userManager.GetRolesAsync(user);
            if (roles.Count == 0)
                await userManager.AddToRoleAsync(user, AppRoles.Administrator);
        }
    }
}

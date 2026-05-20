using Microsoft.AspNetCore.Identity;
using MiniFinance.Data;

namespace MiniFinance.Services;

public static class RoleSeedService
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var user in userManager.Users.ToList())
        {
            if (!string.IsNullOrWhiteSpace(user.WorkspaceOwnerUserId))
                continue;

            var roles = await userManager.GetRolesAsync(user);
            if (roles.Count == 0)
                await userManager.AddToRoleAsync(user, AppRoles.Owner);
        }
    }
}

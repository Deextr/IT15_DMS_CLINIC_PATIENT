using DMS_CPMS.Data.Models;
using Microsoft.AspNetCore.Identity;

namespace DMS_CPMS.Data.Seeders
{
    public static class DatabaseSeeder
    {
        private static readonly string[] Roles = new[] { "SuperAdmin", "Admin", "Staff" };

        public static async Task SeedAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // Ensure roles exist
            foreach (var role in Roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new ApplicationRole { Name = role });
                }
            }

            // Superadmin
            var superAdmin = await EnsureUserAsync(userManager, "Superadmin", "Superadmin123!", "Super", "Admin");
            if (superAdmin != null && !await userManager.IsInRoleAsync(superAdmin, "SuperAdmin"))
            {
                await userManager.AddToRoleAsync(superAdmin, "SuperAdmin");
            }

            // Admin
            var admin = await EnsureUserAsync(userManager, "Admin", "Admin123!", "Admin", "User");
            if (admin != null && !await userManager.IsInRoleAsync(admin, "Admin"))
            {
                await userManager.AddToRoleAsync(admin, "Admin");
            }

            // Staff
            var staff = await EnsureUserAsync(userManager, "Staff", "Staff123!", "Staff", "User");
            if (staff != null && !await userManager.IsInRoleAsync(staff, "Staff"))
            {
                await userManager.AddToRoleAsync(staff, "Staff");
            }
        }

        private static async Task<ApplicationUser?> EnsureUserAsync(UserManager<ApplicationUser> userManager, string userName, string password, string firstName, string lastName)
        {
            var existingUser = await userManager.FindByNameAsync(userName);
            if (existingUser != null)
            {
                return existingUser;
            }

            var user = new ApplicationUser
            {
                UserName = userName,
                Email = $"{userName}@example.com",
                EmailConfirmed = true,
                IsActive = true,
                FirstName = firstName,
                LastName = lastName
            };

            var result = await userManager.CreateAsync(user, password);
            return result.Succeeded ? user : null;
        }
    }
}

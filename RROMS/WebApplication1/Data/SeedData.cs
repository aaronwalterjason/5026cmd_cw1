using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Models.Admin;

namespace RROMS.Data
{
    // Applies pending migrations and seeds the three demo accounts + roles on every startup.
    // Change or remove these demo accounts before submitting coursework - see the table below.
    public static class SeedData
    {
        private static readonly string[] Roles = { "Administrator", "Staff", "Customer" };

        public static async Task InitializeAsync(IServiceProvider services)
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();

            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in Roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

            await EnsureUserAsync(userManager, "admin@rroms.local", "Admin#12345", "Administrator", "Demo Administrator");
            await EnsureUserAsync(userManager, "staff@rroms.local", "Staff#12345", "Staff", "Demo Staff");
            await EnsureUserAsync(userManager, "customer@rroms.local", "Customer#12345", "Customer", "Demo Customer");

            if (!await db.RestaurantSettings.AnyAsync())
            {
                db.RestaurantSettings.Add(new RestaurantSettings());
                await db.SaveChangesAsync();
            }
        }

        private static async Task EnsureUserAsync(UserManager<ApplicationUser> userManager, string email, string password, string role, string fullName)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = fullName };
                var result = await userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to seed demo account '{email}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }

            if (!await userManager.IsInRoleAsync(user, role))
            {
                await userManager.AddToRoleAsync(user, role);
            }
        }
    }
}

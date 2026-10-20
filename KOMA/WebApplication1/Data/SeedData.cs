using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using KOMA.Models;
using KOMA.Services;

namespace KOMA.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in AccountService.Roles)
            if (!await roles.RoleExistsAsync(role)) Check(await roles.CreateAsync(new IdentityRole(role)));

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var accounts = services.GetRequiredService<AccountService>();
        await EnsureAsync("admin@koma.local", "Admin#12345", "Administrator", "Demo Administrator");
        await EnsureAsync("staff@koma.local", "Staff#12345", "Staff", "Demo Staff");
        await EnsureAsync("customer@koma.local", "Customer#12345", "Customer", "Demo Customer");

        // Backfill existing accounts without changing their passwords or assigned role.
        foreach (var user in await users.Users.ToListAsync())
        {
            var assigned = await users.GetRolesAsync(user);
            if (assigned.Count > 1 || assigned.Any(r => !AccountService.Roles.Contains(r)))
                throw new InvalidOperationException($"Account {user.Id} must have one supported role before profiles can be created.");
            Check(await accounts.SetRoleAsync(user, assigned.SingleOrDefault() ?? "Customer"));
        }
        if (!await db.RestaurantSettings.AnyAsync())
        {
            db.RestaurantSettings.Add(new RestaurantSettings());
            await db.SaveChangesAsync();
        }

        // Sample orders for the Order List / Revenue Dashboard (Development only, see DemoOrderSeeder).
        if (services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            foreach (var (email, name) in DemoOrderSeeder.DemoCustomers)
                await EnsureAsync(email, "Customer#12345", "Customer", name);
            await DemoOrderSeeder.SeedAsync(db);
            await DemoOrderSeeder.EnsureTodayAsync(db);
        }

        async Task EnsureAsync(string email, string password, string role, string name)
        {
            if (await users.FindByEmailAsync(email) != null) return;
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = name };
            Check(await accounts.CreateAsync(user, password, role));
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}

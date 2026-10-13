using System.Data;
using KOMA.Data;
using KOMA.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Services;

// All application account creation and role changes go through this service.
// The transaction keeps Identity roles and the single matching profile together.
public class AccountService(ApplicationDbContext db, UserManager<ApplicationUser> users)
{
    public static readonly string[] Roles = ["Administrator", "Staff", "Customer"];

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, string password, string role)
    {
        if (!Roles.Contains(role)) return Failure("Choose a valid account role.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) return result;
        result = await users.AddToRoleAsync(user, role);
        if (!result.Succeeded) return result;
        await SetProfileAsync(user.Id, role);
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> SetRoleAsync(ApplicationUser user, string role)
    {
        if (!Roles.Contains(role)) return Failure("Choose a valid account role.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var current = await users.GetRolesAsync(user);
        if (current.Contains("Administrator") && role != "Administrator" &&
            (await users.GetUsersInRoleAsync("Administrator")).Count <= 1)
            return Failure("Keep at least one administrator account.");

        var remove = current.Where(r => r != role).ToArray();
        if (remove.Length > 0)
        {
            var result = await users.RemoveFromRolesAsync(user, remove);
            if (!result.Succeeded) return result;
        }
        if (!current.Contains(role))
        {
            var result = await users.AddToRoleAsync(user, role);
            if (!result.Succeeded) return result;
        }
        await SetProfileAsync(user.Id, role);
        if (remove.Length > 0 || !current.Contains(role))
        {
            var result = await users.UpdateSecurityStampAsync(user);
            if (!result.Succeeded) return result;
        }
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }

    private async Task SetProfileAsync(string userId, string role)
    {
        var customer = await db.Customers.FindAsync(userId);
        var staff = await db.Staffs.FindAsync(userId);
        var administrator = await db.Administrators.FindAsync(userId);
        if (role != "Customer" && customer != null) db.Customers.Remove(customer);
        if (role != "Staff" && staff != null) db.Staffs.Remove(staff);
        if (role != "Administrator" && administrator != null) db.Administrators.Remove(administrator);
        if (role == "Customer" && customer == null) db.Customers.Add(new Customer { UserId = userId });
        if (role == "Staff" && staff == null) db.Staffs.Add(new Staff { UserId = userId });
        if (role == "Administrator" && administrator == null) db.Administrators.Add(new Administrator { UserId = userId });
        await db.SaveChangesAsync();
    }

    private static IdentityResult Failure(string description) =>
        IdentityResult.Failed(new IdentityError { Description = description });
}

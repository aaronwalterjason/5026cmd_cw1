using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services;

namespace KOMA.Pages.Admin.Accounts
{
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AccountService _accounts;
        private readonly ApplicationDbContext _db;
        public IndexModel(UserManager<ApplicationUser> userManager, AccountService accounts, ApplicationDbContext db)
        {
            _userManager = userManager;
            _accounts = accounts;
            _db = db;
        }

        public List<AccountRow> Accounts { get; set; } = new();
        public string[] AvailableRoles { get; } = { "Administrator", "Staff", "Customer" };

        public async Task OnGetAsync()
        {
            await LoadAccountsAsync();
        }

        public async Task<IActionResult> OnPostAsync(string userId, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is not null)
            {
                var oldRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "Customer";
                var result = await _accounts.SetRoleAsync(user, role);
                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
                    await LoadAccountsAsync();
                    return Page();
                }
                TempData["StatusMessage"] = $"{user.Email} is now {role}.";
                if (oldRole != role) await LogRoleChangeAsync(_db, User, user, oldRole, role, null);
            }

            return RedirectToPage();
        }

        private async Task LoadAccountsAsync()
        {
            foreach (var user in _userManager.Users.ToList())
            {
                var roles = await _userManager.GetRolesAsync(user);
                Accounts.Add(new AccountRow(user.Id, user.Email ?? user.UserName ?? "(no email)", roles.FirstOrDefault() ?? "Customer"));
            }
        }

        public record AccountRow(string UserId, string Email, string Role);

        // Admin > Activity entry for a role change (also used when an admin reverts one).
        public static async Task<ActivityLog> LogRoleChangeAsync(ApplicationDbContext db, System.Security.Claims.ClaimsPrincipal by,
            ApplicationUser target, string oldRole, string newRole, int? revertOf)
        {
            var who = AuditInterceptor.StaffOrAdmin(by);
            var me = who?.Id is null ? null : await db.Users.FindAsync(who.Value.Id);
            var log = new ActivityLog
            {
                UserId = who?.Id, UserRole = who?.Role, UserName = me?.FullName ?? me?.Email ?? by.Identity?.Name ?? "Unknown",
                Area = ActivityLog.Areas.Settings, Action = revertOf.HasValue ? "Reverted" : "Role changed",
                Title = target.FullName ?? target.Email ?? target.Id, Summary = $"{oldRole} → {newRole}",
                RevertOfId = revertOf,
                SnapshotsJson = System.Text.Json.JsonSerializer.Serialize(new[] { new ActivitySnapshot
                {
                    Type = "Role", Label = target.Email ?? target.Id, State = "Modified", Changed = ["Role"],
                    Key = new() { ["UserId"] = System.Text.Json.JsonSerializer.SerializeToElement(target.Id) },
                    Before = new() { ["Role"] = System.Text.Json.JsonSerializer.SerializeToElement(oldRole) },
                    After = new() { ["Role"] = System.Text.Json.JsonSerializer.SerializeToElement(newRole) }
                } })
            };
            db.ActivityLogs.Add(log);
            await db.SaveChangesAsync();
            return log;
        }
    }
}

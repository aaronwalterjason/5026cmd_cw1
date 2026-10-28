using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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

        // Accounts landing: pick a group (Customers / Staff / Admins) first, then see that group's accounts.
        // ?group=customers|staff|admins - empty shows the three group cards.
        [BindProperty(SupportsGet = true)] public string? Group { get; set; }

        public record GroupInfo(string Key, string Title, string Role, string Subtitle);
        public static readonly GroupInfo[] Groups =
        {
            new("customers", "Customers", "Customer", "accounts"),
            new("staff", "Staff", "Staff", "team members"),
            new("admins", "Admins", "Administrator", "with full access"),
        };
        public GroupInfo? CurrentGroup => Groups.FirstOrDefault(g => g.Key.Equals(Group, StringComparison.OrdinalIgnoreCase));
        public int CountFor(GroupInfo g) => Accounts.Count(a => a.Role == g.Role);
        public IEnumerable<AccountRow> Shown => CurrentGroup is null ? Accounts
            : Accounts.Where(a => a.Role == CurrentGroup.Role).OrderBy(a => DisplayName(a), StringComparer.OrdinalIgnoreCase);

        public async Task OnGetAsync()
        {
            await LoadAccountsAsync();
            if (CurrentGroup is not null) await LoadStatsAsync();
        }

        // ---------- ID badges (one card per person) ----------
        public record BadgeStat(string Label, string Value);
        private readonly Dictionary<string, (BadgeStat, BadgeStat)> _stats = new();
        public (BadgeStat First, BadgeStat Second) StatsFor(string userId) =>
            _stats.TryGetValue(userId, out var s) ? s
            : CurrentGroup?.Role == "Customer" ? (new("Bookings", "0"), new("Last visit", "—"))
            : (new("Last active", "—"), new("Changes · 7 days", "0"));

        public string? MyId => _userManager.GetUserId(User);
        public static string BadgeLabel(GroupInfo g) => g.Role == "Administrator" ? "ADMIN" : g.Role.ToUpperInvariant();
        public static string DisplayName(AccountRow a) =>
            !string.IsNullOrWhiteSpace(a.FullName) ? a.FullName! : a.Email.Split('@')[0];
        public static string Initials(AccountRow a)
        {
            var parts = DisplayName(a).Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var s = string.Concat(parts.Take(2).Select(w => char.ToUpperInvariant(w[0])));
            return s.Length > 0 ? s : "?";
        }

        // "10 min ago", "Yesterday", "3 days ago", "Mar 2026". dayOnly: say "Today" instead of minutes/hours.
        public static string Ago(DateTime? t, bool dayOnly = false)
        {
            if (t is null) return "—";
            var now = DateTime.Now;
            var days = (now.Date - t.Value.Date).Days;
            if (days <= 0)
            {
                if (dayOnly) return "Today";
                var mins = (int)(now - t.Value).TotalMinutes;
                return mins < 1 ? "Now" : mins < 60 ? $"{mins} min ago" : $"{mins / 60} h ago";
            }
            if (days == 1) return "Yesterday";
            if (days < 7) return $"{days} days ago";
            if (days < 31) return days / 7 == 1 ? "1 week ago" : $"{days / 7} weeks ago";
            return t.Value.ToString("MMM yyyy");
        }

        // Two numbers per badge, worked out for the people on this page only (a few grouped queries).
        // Customers: bookings made (not cancelled) and their last visit (latest past booking or order).
        // Staff / Admins: when they last did something in Admin > Activity, and how many changes in 7 days.
        private async Task LoadStatsAsync()
        {
            var ids = Shown.Select(a => a.UserId).ToList();
            if (ids.Count == 0) return;
            var now = DateTime.Now;

            if (CurrentGroup!.Role == "Customer")
            {
                var bookings = await _db.Reservations.AsNoTracking()
                    .Where(r => r.CustomerUserId != null && ids.Contains(r.CustomerUserId) && r.Status != ReservationStatus.Cancelled)
                    .GroupBy(r => r.CustomerUserId!)
                    .Select(g => new { Id = g.Key, Count = g.Count(), Last = g.Max(r => r.StartTime <= now ? (DateTime?)r.StartTime : null) })
                    .ToListAsync();
                var orders = await _db.Orders.AsNoTracking()
                    .Where(o => o.CustomerId != null && ids.Contains(o.CustomerId) && o.Status != OrderStatus.Cancelled)
                    .GroupBy(o => o.CustomerId!)
                    .Select(g => new { Id = g.Key, Last = g.Max(o => o.OrderedAt) })
                    .ToListAsync();
                var b = bookings.ToDictionary(x => x.Id);
                var o = orders.ToDictionary(x => x.Id, x => (DateTime?)x.Last);
                foreach (var id in ids)
                {
                    b.TryGetValue(id, out var bk);
                    o.TryGetValue(id, out var lastOrder);
                    DateTime? last = bk?.Last;
                    if (lastOrder is not null && (last is null || lastOrder > last)) last = lastOrder;
                    _stats[id] = (new("Bookings", (bk?.Count ?? 0).ToString()), new("Last visit", Ago(last, dayOnly: true)));
                }
            }
            else
            {
                var weekAgo = now.AddDays(-7);
                var logs = await _db.ActivityLogs.AsNoTracking()
                    .Where(l => l.UserId != null && ids.Contains(l.UserId))
                    .GroupBy(l => l.UserId!)
                    .Select(g => new { Id = g.Key, Last = g.Max(l => l.At), Week = g.Count(l => l.At >= weekAgo) })
                    .ToListAsync();
                var byId = logs.ToDictionary(x => x.Id);
                foreach (var id in ids)
                {
                    byId.TryGetValue(id, out var l);
                    _stats[id] = (new("Last active", Ago(l?.Last)), new("Changes · 7 days", (l?.Week ?? 0).ToString()));
                }
            }
        }

        public async Task<IActionResult> OnPostAsync(string userId, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is not null)
            {
                var oldRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "Customer";
                // Customers sign up themselves, so they are never promoted to Staff/Admin from here -
                // a staff member gets their own work account instead.
                var result = oldRole == "Customer" && role != "Customer"
                    ? IdentityResult.Failed(new IdentityError { Description = "Customer accounts can't be changed to Staff or Admin. Create a separate staff account instead." })
                    : await _accounts.SetRoleAsync(user, role);
                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
                    await LoadAccountsAsync();
                    if (CurrentGroup is not null) await LoadStatsAsync();
                    return Page();
                }
                TempData["StatusMessage"] = $"{user.Email} is now {role}.";
                if (oldRole != role) await LogRoleChangeAsync(_db, User, user, oldRole, role, null);
            }

            return RedirectToPage(new { group = CurrentGroup?.Key });   // stay in the same group
        }

        // One query for every account and its role (instead of a separate role lookup per user).
        // Accounts with no role count as Customer.
        private async Task LoadAccountsAsync()
        {
            var roleOf = await (from ur in _db.UserRoles
                                join r in _db.Roles on ur.RoleId equals r.Id
                                select new { ur.UserId, r.Name })
                               .AsNoTracking().ToListAsync();
            var roleByUser = roleOf.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.First().Name);

            var users = await _db.Users.AsNoTracking()
                .Select(u => new { u.Id, u.Email, u.UserName, u.FullName, u.PhoneNumber }).ToListAsync();

            Accounts = users.Select(u => new AccountRow(u.Id, u.Email ?? u.UserName ?? "(no email)",
                roleByUser.TryGetValue(u.Id, out var role) && role is not null ? role : "Customer",
                u.FullName, u.PhoneNumber)).ToList();
        }

        public record AccountRow(string UserId, string Email, string Role, string? FullName = null, string? Phone = null);

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

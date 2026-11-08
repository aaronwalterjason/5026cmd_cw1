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
    public partial class IndexModel : PageModel
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
            if (IsSchedule) await LoadScheduleAsync();   // Staff > Schedule (Index.Schedule.cs)
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

        // ---------- Customer details: the panel that slides in from the right ----------
        // Opened by clicking a customer badge. The panel HTML comes from OnGetDetailAsync (?handler=Detail&id=...),
        // and ?group=customers&id=... opens it straight away, so a customer can be bookmarked or shared.
        [BindProperty(SupportsGet = true, Name = "id")] public string? OpenId { get; set; }

        public record PanelBooking(int Id, DateTime Start, int Guests, string Table, string? Request, ReservationStatus Status, bool Attended);
        public record PanelOrder(int Id, DateTime At, string Type, string Items, decimal Total, PaymentStatus Paid, bool Cancelled);
        public record PanelAsk(string Text, int Count, DateTime Last, bool LastCancelled);
        public record PanelDish(string Name, int Qty);

        public class CustomerPanel
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Initials { get; set; } = "?";
            public string Email { get; set; } = "";
            public bool EmailConfirmed { get; set; }
            public string? Phone { get; set; }
            public bool PhoneConfirmed { get; set; }
            public bool Locked { get; set; }
            public int FailedSignIns { get; set; }
            public DateTime? Since { get; set; }
            public string SinceFrom { get; set; } = "";
            public DateTime? LastVisit { get; set; }

            public int Visits { get; set; }
            public decimal Spent { get; set; }
            public int PaidOrders { get; set; }
            public decimal? AvgPerVisit => Visits > 0 ? Math.Round(Spent / Visits, 2) : null;
            public int PastBookings { get; set; }       // past, not cancelled
            public int ShowedUp { get; set; }           // of those, checked in or ordered against
            public int? ShowedPct => PastBookings > 0 ? (int)Math.Round(ShowedUp * 100.0 / PastBookings) : null;

            public List<PanelBooking> Bookings { get; set; } = new();     // newest first
            public List<PanelBooking> Upcoming { get; set; } = new();
            public List<PanelBooking> Recent { get; set; } = new();
            public List<PanelAsk> Asks { get; set; } = new();
            public List<PanelOrder> Orders { get; set; } = new();         // newest first
            public List<PanelDish> Dishes { get; set; } = new();
            public string? UsualParty { get; set; }
            public string? UsualTime { get; set; }
            public string? FavouriteSpot { get; set; }
            public string? PaysBy { get; set; }
        }

        public async Task<IActionResult> OnGetDetailAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();
            var u = await _userManager.FindByIdAsync(id);
            if (u is null) return NotFound();
            var role = (await _userManager.GetRolesAsync(u)).FirstOrDefault() ?? "Customer";
            if (role != "Customer")
                return Partial("_StaffPanel", await BuildStaffPanelAsync(u, role));   // Staff / Admin card
            var panel = await BuildPanelAsync(id);
            return panel is null ? NotFound() : Partial("_CustomerPanel", panel);
        }

        private async Task<CustomerPanel?> BuildPanelAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (u is null) return null;
            var now = DateTime.Now;

            // A booking counts as "attended" when staff seated it (table session) or an order was placed against it.
            var seated = await _db.TableServiceSessions.AsNoTracking()
                .Where(s => s.ReservationId != null && s.Reservation!.CustomerUserId == id)
                .Select(s => s.ReservationId!.Value).ToListAsync();
            var ordered = await _db.Orders.AsNoTracking()
                .Where(o => o.ReservationId != null && o.Reservation!.CustomerUserId == id && o.Status != OrderStatus.Cancelled)
                .Select(o => o.ReservationId!.Value).ToListAsync();
            var attended = seated.Concat(ordered).ToHashSet();

            var bookings = (await _db.Reservations.AsNoTracking()
                .Where(r => r.CustomerUserId == id)
                .OrderByDescending(r => r.StartTime)
                .Select(r => new { r.Id, r.StartTime, r.GuestCount, r.SpecialRequests, r.Status, r.CreatedAtUtc,
                    Table = r.RestaurantTable.TableNumber, Area = r.RestaurantTable.DiningArea != null ? r.RestaurantTable.DiningArea.Name : null })
                .ToListAsync());
            var b = bookings.Select(r => new PanelBooking(r.Id, r.StartTime, r.GuestCount,
                string.IsNullOrEmpty(r.Area) ? r.Table : $"{r.Table} · {r.Area}", r.SpecialRequests, r.Status, attended.Contains(r.Id))).ToList();

            var orders = await _db.Orders.AsNoTracking()
                .Where(o => o.CustomerId == id)
                .OrderByDescending(o => o.OrderedAt)
                .Select(o => new { o.Id, o.OrderedAt, o.OrderType, o.TotalAmount, o.PaymentStatus, o.PaymentMethod, o.Status,
                    Items = o.Items.Select(i => new { i.ItemName, i.Quantity, Dish = i.MenuItem != null ? i.MenuItem.Name : null }).ToList() })
                .ToListAsync();

            var p = new CustomerPanel
            {
                Id = u.Id,
                Name = !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName! : (u.Email ?? u.UserName ?? "?").Split('@')[0],
                Email = u.Email ?? u.UserName ?? "",
                EmailConfirmed = u.EmailConfirmed,
                Phone = u.PhoneNumber,
                PhoneConfirmed = u.PhoneNumberConfirmed,
                Locked = u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow,
                FailedSignIns = u.AccessFailedCount,
                Bookings = b,
            };
            p.Initials = Initials(new AccountRow(u.Id, p.Email, "Customer", p.Name));

            var live = b.Where(x => x.Status != ReservationStatus.Cancelled).ToList();
            var past = live.Where(x => x.Start <= now).ToList();
            p.PastBookings = past.Count;
            p.ShowedUp = past.Count(x => x.Attended);
            p.Upcoming = live.Where(x => x.Start > now).OrderBy(x => x.Start).Take(3).ToList();
            p.Recent = b.Where(x => x.Start <= now).Take(3).ToList();

            // Things they ask for: the same special request (ignoring case/spaces) counted across bookings.
            p.Asks = b.Where(x => !string.IsNullOrWhiteSpace(x.Request))
                .GroupBy(x => x.Request!.Trim().TrimEnd('.').ToLowerInvariant())
                .Select(g => { var last = g.OrderByDescending(x => x.Start).First();
                    return new PanelAsk(last.Request!.Trim(), g.Count(), last.Start, last.Status == ReservationStatus.Cancelled); })
                .OrderByDescending(a => a.Count).ThenByDescending(a => a.Last).Take(5).ToList();

            var liveOrders = orders.Where(o => o.Status != OrderStatus.Cancelled).ToList();
            p.Orders = orders.Select(o =>
            {
                var names = o.Items.Select(i => (Name: !string.IsNullOrWhiteSpace(i.ItemName) ? i.ItemName : i.Dish ?? "Item", i.Quantity)).ToList();
                var text = string.Join(", ", names.Take(2).Select(n => n.Quantity > 1 ? $"{n.Name} ×{n.Quantity}" : n.Name));
                if (names.Count > 2) text += $", +{names.Count - 2} more";
                return new PanelOrder(o.Id, o.OrderedAt, o.OrderType.DisplayName(), text, o.TotalAmount, o.PaymentStatus, o.Status == OrderStatus.Cancelled);
            }).ToList();
            var paid = liveOrders.Where(o => o.PaymentStatus == PaymentStatus.Paid).ToList();
            p.Spent = paid.Sum(o => o.TotalAmount);
            p.PaidOrders = paid.Count;

            // Visits: different days they came in (an attended booking or an order).
            var visitDays = past.Where(x => x.Attended).Select(x => x.Start.Date)
                .Concat(liveOrders.Select(o => o.OrderedAt.Date)).Distinct().ToList();
            p.Visits = visitDays.Count;
            p.LastVisit = visitDays.Count > 0 ? visitDays.Max() : null;

            // Customer since: their first booking or order (accounts don't store a sign-up date).
            var firstBooking = bookings.Count > 0 ? bookings.Min(x => x.CreatedAtUtc).ToLocalTime() : (DateTime?)null;
            var firstOrder = orders.Count > 0 ? orders.Min(o => o.OrderedAt) : (DateTime?)null;
            if (firstBooking is not null && (firstOrder is null || firstBooking <= firstOrder)) { p.Since = firstBooking; p.SinceFrom = "first booking"; }
            else if (firstOrder is not null) { p.Since = firstOrder; p.SinceFrom = "first order"; }

            // Habits
            p.Dishes = liveOrders.SelectMany(o => o.Items)
                .GroupBy(i => !string.IsNullOrWhiteSpace(i.ItemName) ? i.ItemName : i.Dish ?? "Item")
                .Select(g => new PanelDish(g.Key, g.Sum(i => i.Quantity)))
                .OrderByDescending(d => d.Qty).Take(3).ToList();
            if (live.Count > 0)
            {
                var party = live.GroupBy(x => x.Guests).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
                p.UsualParty = party == 1 ? "1 person" : $"{party} people";
                p.FavouriteSpot = live.GroupBy(x => x.Table).OrderByDescending(g => g.Count()).First().Key;
            }
            var times = live.Select(x => x.Start).Concat(liveOrders.Select(o => o.OrderedAt)).ToList();
            if (times.Count > 0)
            {
                var days = times.GroupBy(t => t.DayOfWeek).OrderByDescending(g => g.Count()).Take(2)
                    .OrderBy(g => ((int)g.Key + 6) % 7).Select(g => g.Key.ToString()[..3]);
                var part = times.GroupBy(t => t.Hour < 15 ? "lunch" : t.Hour < 18 ? "afternoons" : "evenings")
                    .OrderByDescending(g => g.Count()).First().Key;
                p.UsualTime = $"{string.Join(", ", days)} · {part}";
            }
            var methods = paid.Where(o => o.PaymentMethod != null).ToList();
            if (methods.Count > 0)
            {
                var top = methods.GroupBy(o => o.PaymentMethod!.Value).OrderByDescending(g => g.Count()).First();
                p.PaysBy = $"{top.Key.DisplayName()} ({Math.Round(top.Count() * 100.0 / methods.Count)}%)";
            }
            return p;
        }

        // ---------- Staff / Admin details: same slide-in panel (pine band for Staff, vermilion for Admins) ----------
        // Activity and sign-ins come from Admin > Activity (ActivityLogs for this user); help requests done from HelpRequests.DoneBy.
        public record StaffAct(int Id, DateTime At, string Area, string Action, string Title, string? Summary, bool Reverted);
        public record StaffSignIn(DateTime At, string Action, string? Device, bool Ok);

        public class StaffPanel
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Initials { get; set; } = "?";
            public string Email { get; set; } = "";
            public bool EmailConfirmed { get; set; }
            public string? Phone { get; set; }
            public string Role { get; set; } = "Staff";          // "Staff" | "Administrator"
            public bool IsAdmin => Role == "Administrator";
            public bool IsMe { get; set; }
            public bool Locked { get; set; }
            public int FailedSignIns { get; set; }
            public DateTime? LastActive { get; set; }
            public DateTime? FirstSeen { get; set; }
            public int Changes7 { get; set; }
            public int SignIns7 { get; set; }
            public int HelpDone { get; set; }
            public int AdminCount { get; set; }
            public List<StaffAct> Activity { get; set; } = new();
            public List<StaffSignIn> SignIns { get; set; } = new();
            // Schedule tab (staff only, see Index.Schedule.cs)
            public bool HasSchedule { get; set; }
            public DateOnly SchedMonth { get; set; }
            public List<PanelShiftDay> SchedDays { get; set; } = new();
            public PanelShiftDay? NextShift { get; set; }
            public int ShiftsMonth { get; set; }
            public double HoursMonth { get; set; }
            public int LeaveMonth { get; set; }
        }

        private async Task<StaffPanel?> BuildStaffPanelAsync(ApplicationUser u, string role)
        {
            var weekAgo = DateTime.Now.AddDays(-7);
            var logs = _db.ActivityLogs.AsNoTracking().Where(l => l.UserId == u.Id);
            var stats = await logs.GroupBy(l => 1).Select(g => new
            {
                Last = g.Max(l => (DateTime?)l.At),
                First = g.Min(l => (DateTime?)l.At),
                Changes = g.Count(l => l.At >= weekAgo && l.Area != ActivityLog.Areas.SignIns),
                SignIns = g.Count(l => l.At >= weekAgo && l.Area == ActivityLog.Areas.SignIns && l.Action == "Signed in")
            }).FirstOrDefaultAsync();
            var activity = await logs.Where(l => l.Area != ActivityLog.Areas.SignIns).OrderByDescending(l => l.At).Take(25)
                .Select(l => new StaffAct(l.Id, l.At, l.Area, l.Action, l.Title, l.Summary, l.RevertedById != null)).ToListAsync();
            var signIns = await logs.Where(l => l.Area == ActivityLog.Areas.SignIns).OrderByDescending(l => l.At).Take(12)
                .Select(l => new StaffSignIn(l.At, l.Action, l.Summary, l.Action == "Signed in")).ToListAsync();
            var shortName = (u.UserName ?? u.Email ?? "").Split('@')[0];
            int helpDone = 0;
            try { helpDone = await _db.HelpRequests.CountAsync(h => h.DoneBy == shortName); } catch { /* table not created yet */ }

            var p = new StaffPanel
            {
                Id = u.Id,
                Name = !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName! : (u.Email ?? u.UserName ?? "?").Split('@')[0],
                Email = u.Email ?? u.UserName ?? "",
                EmailConfirmed = u.EmailConfirmed,
                Phone = u.PhoneNumber,
                Role = role,
                IsMe = u.Id == MyId,
                Locked = u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow,
                FailedSignIns = u.AccessFailedCount,
                LastActive = stats?.Last, FirstSeen = stats?.First,
                Changes7 = stats?.Changes ?? 0, SignIns7 = stats?.SignIns ?? 0, HelpDone = helpDone,
                AdminCount = (await _userManager.GetUsersInRoleAsync("Administrator")).Count,
                Activity = activity, SignIns = signIns
            };
            p.Initials = Initials(new AccountRow(u.Id, p.Email, role, p.Name));
            if (!p.IsAdmin) await FillPanelScheduleAsync(p);
            return p;
        }

        // A one-time link the admin can pass on so a staff member sets a new password
        // (there is no email sending yet). Opens Identity's Reset password page.
        public async Task<IActionResult> OnPostResetLinkAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return NotFound();
            var role = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "Customer";
            if (role == "Customer") return Forbid();
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var code = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));
            var url = Url.Page("/Account/ResetPassword", null, new { area = "Identity", code }, Request.Scheme);
            return new JsonResult(new { url });
        }

        // Lock / unlock someone's sign-in from the panel. Never yourself, and never the last admin who can still sign in.
        public async Task<IActionResult> OnPostLockAsync(string userId, bool lockIt)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return NotFound();
            var role = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "Customer";
            if (user.Id == MyId) return Forbid();
            var groupKey = Groups.FirstOrDefault(g => g.Role == role)?.Key ?? "customers";
            if (lockIt && role == "Administrator")
            {
                var openAdmins = (await _userManager.GetUsersInRoleAsync("Administrator"))
                    .Count(a => !(a.LockoutEnd.HasValue && a.LockoutEnd.Value > DateTimeOffset.UtcNow));
                if (openAdmins <= 1)
                {
                    TempData["StatusMessage"] = "The last admin can't be locked.";
                    return RedirectToPage(new { group = groupKey, id = user.Id });
                }
            }
            if (lockIt)
            {
                await _userManager.SetLockoutEnabledAsync(user, true);
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                await _userManager.UpdateSecurityStampAsync(user);   // signs them out on their next request check
                TempData["StatusMessage"] = $"{user.Email} is locked and can't sign in.";
            }
            else
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                await _userManager.ResetAccessFailedCountAsync(user);
                TempData["StatusMessage"] = $"{user.Email} can sign in again.";
            }
            return RedirectToPage(new { group = groupKey, id = user.Id });
        }

        public async Task<IActionResult> OnPostAsync(string userId, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is not null)
            {
                var oldRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "Customer";
                if (user.Id == MyId && role != oldRole)
                {
                    TempData["StatusMessage"] = "You can't change your own role.";
                    return RedirectToPage(new { group = CurrentGroup?.Key });
                }
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
                // Changed from the details panel: go to their new group with the panel open again.
                if (Request.HasFormContentType && Request.Form.ContainsKey("panel"))
                    return RedirectToPage(new { group = Groups.FirstOrDefault(g => g.Role == role)?.Key, id = user.Id });
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

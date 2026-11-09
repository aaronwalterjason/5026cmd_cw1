using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services;

namespace KOMA.Pages.Admin.Settings
{
    // Admin > Settings (design A): opening hours per day (open/closed + times), last booking time,
    // closed dates (holidays, events) and the announcement banner.
    // Saving is checked: closing after opening, at least one open day, at least one hour open.
    // Bookings already made that now fall outside the hours are reported, never moved automatically.
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public class DayInput
        {
            public bool Open { get; set; } = true;
            public string Opens { get; set; } = "11:00";
            public string Closes { get; set; } = "22:00";
        }

        [BindProperty] public List<DayInput> Days { get; set; } = new();
        [BindProperty] public int LastBookingMinutes { get; set; } = 75;
        [BindProperty] public string? AnnouncementText { get; set; }

        // Add closed date (separate small form)
        [BindProperty] public DateOnly? NewFrom { get; set; }
        [BindProperty] public DateOnly? NewTo { get; set; }
        [BindProperty] public string? NewReason { get; set; }

        public List<RestaurantSettings.ClosedPeriod> Closed { get; private set; } = new();
        public List<(DateTime Start, string Name)> Outside { get; private set; } = new();
        public string? DayError(int i) => ModelState.TryGetValue($"Days[{i}].Closes", out var e) && e.Errors.Count > 0 ? e.Errors[0].ErrorMessage : null;

        public static readonly (int Minutes, string Label)[] LastOptions =
        {
            (60, "1 hour before closing"), (75, "1 h 15 min before closing"), (90, "1 h 30 min before closing"), (120, "2 hours before closing")
        };

        // 06:00 … 23:30 every 30 minutes, plus 23:59 for "midnight".
        public static IEnumerable<string> TimeOptions =>
            Enumerable.Range(12, 36).Select(i => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(i * 30)).ToString("HH:mm", CultureInfo.InvariantCulture)).Append("23:59");

        public static string Label12(string hhmm)
        {
            if (hhmm == "23:59") return "Midnight";
            var t = TimeOnly.ParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture);
            var h = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
            return t.Minute == 0 ? $"{h} {(t.Hour < 12 ? "am" : "pm")}" : $"{h}:{t.Minute:00} {(t.Hour < 12 ? "am" : "pm")}";
        }

        public async Task OnGetAsync()
        {
            var s = await GetOrCreateSettingsAsync();
            Days = s.Week().Select(d => new DayInput
            {
                Open = d.IsOpen,
                Opens = d.Opens.ToString("HH:mm", CultureInfo.InvariantCulture),
                Closes = d.Closes.ToString("HH:mm", CultureInfo.InvariantCulture)
            }).ToList();
            LastBookingMinutes = s.LastBookingMinutes;
            AnnouncementText = s.AnnouncementText;
            await LoadExtrasAsync(s);
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var s = await GetOrCreateSettingsAsync();
            if (Days.Count != 7) return BadRequest();

            var week = new List<RestaurantSettings.DayHours>();
            for (var i = 0; i < 7; i++)
            {
                var d = Days[i];
                var okOpen = TimeOnly.TryParseExact(d.Opens, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var opens);
                var okClose = TimeOnly.TryParseExact(d.Closes, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var closes);
                if (!okOpen || !okClose) { opens = new(11, 0); closes = new(22, 0); }
                if (d.Open)
                {
                    if (!okOpen || !okClose) ModelState.AddModelError($"Days[{i}].Closes", "Choose an opening and a closing time.");
                    else if (closes <= opens) ModelState.AddModelError($"Days[{i}].Closes", "Closing must be after opening.");
                    else if ((closes - opens).TotalMinutes < ReservationRules.SlotMinutes) ModelState.AddModelError($"Days[{i}].Closes", "Open for at least 1 hour.");
                }
                week.Add(new RestaurantSettings.DayHours(d.Open, opens, closes));
            }
            if (!week.Any(w => w.IsOpen)) ModelState.AddModelError(string.Empty, "Open on at least one day.");
            if (!LastOptions.Any(o => o.Minutes == LastBookingMinutes)) LastBookingMinutes = 75;
            if (AnnouncementText?.Length > 300) ModelState.AddModelError(nameof(AnnouncementText), "Keep the announcement under 300 characters.");

            // Only the week form is validated here (the closed-date fields belong to the other form).
            foreach (var k in new[] { nameof(NewFrom), nameof(NewTo), nameof(NewReason) }) ModelState.Remove(k);
            if (!ModelState.IsValid)
            {
                await LoadExtrasAsync(s);
                return Page();
            }

            var open = week.Where(w => w.IsOpen).ToList();
            s.WeeklyHours = RestaurantSettings.Format(week);
            s.OpeningTime = open.Min(w => w.Opens);
            s.ClosingTime = open.Max(w => w.Closes);
            s.LastBookingMinutes = LastBookingMinutes;
            s.AnnouncementText = string.IsNullOrWhiteSpace(AnnouncementText) ? null : AnnouncementText.Trim();
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = "Settings saved.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostAddClosedAsync()
        {
            var s = await GetOrCreateSettingsAsync();
            var today = DateOnly.FromDateTime(ReservationRules.MalaysiaNow);
            var reason = NewReason?.Trim();
            var to = NewTo ?? NewFrom;
            string? error = NewFrom is null ? "Choose the first closed date."
                : NewFrom < today ? "Closed dates must be today or later."
                : to < NewFrom ? "The last date can't be before the first date."
                : to!.Value.DayNumber - NewFrom.Value.DayNumber > 60 ? "Close for at most 60 days at a time."
                : string.IsNullOrWhiteSpace(reason) ? "Add a reason, e.g. Christmas or Staff training."
                : reason!.Length > 60 ? "Keep the reason under 60 characters." : null;
            if (error is not null)
            {
                TempData["StatusError"] = error;
                return RedirectToPage(null, null, null, "closed");
            }
            var list = s.Closures();
            list.Add(new RestaurantSettings.ClosedPeriod(NewFrom!.Value, to!.Value, reason!));
            s.SetClosures(list);
            await _db.SaveChangesAsync();
            TempData["StatusMessage"] = $"Closed {list.Last(c => c.From == NewFrom).Label} added.";
            return RedirectToPage(null, null, null, "closed");
        }

        public async Task<IActionResult> OnPostRemoveClosedAsync(DateOnly from, DateOnly to)
        {
            var s = await GetOrCreateSettingsAsync();
            var list = s.Closures();
            var item = list.FirstOrDefault(c => c.From == from && c.To == to);
            if (item is not null)
            {
                list.Remove(item);
                s.SetClosures(list);
                await _db.SaveChangesAsync();
                TempData["StatusMessage"] = $"{item.Label} is open again.";
            }
            return RedirectToPage(null, null, null, "closed");
        }

        // Closed dates that haven't ended, and upcoming bookings that are outside the saved hours.
        private async Task LoadExtrasAsync(RestaurantSettings s)
        {
            var now = ReservationRules.MalaysiaNow;
            var today = DateOnly.FromDateTime(now);
            Closed = s.Closures().Where(c => c.To >= today).ToList();
            var upcoming = await _db.Reservations.AsNoTracking()
                .Where(r => r.StartTime >= now && r.StartTime < now.AddDays(120)
                            && r.Status != ReservationStatus.Cancelled && r.Status != ReservationStatus.NoShow)
                .OrderBy(r => r.StartTime).Select(r => new { r.StartTime, r.CustomerName }).ToListAsync();
            Outside = upcoming.Where(r =>
            {
                var plan = s.For(DateOnly.FromDateTime(r.StartTime));
                var t = TimeOnly.FromDateTime(r.StartTime);
                return !plan.IsOpen || t < plan.Opens || t > s.LastBookingFor(plan.Closes);
            }).Select(r => (r.StartTime, r.CustomerName)).ToList();
        }

        private async Task<RestaurantSettings> GetOrCreateSettingsAsync()
        {
            var settings = await _db.RestaurantSettings.FirstOrDefaultAsync();
            if (settings is null)
            {
                settings = new RestaurantSettings();
                _db.RestaurantSettings.Add(settings);
                await _db.SaveChangesAsync();
            }
            return settings;
        }
    }
}

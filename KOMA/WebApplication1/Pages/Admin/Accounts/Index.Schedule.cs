using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KOMA.Models;

namespace KOMA.Pages.Admin.Accounts
{
    // Staff duty schedule: Admin > Accounts > Staff > Schedule (?group=staff&view=schedule&week=yyyy-MM-dd&m=yyyy-MM).
    // A week table (staff x Mon-Sun) with a day strip under it to move between weeks and months.
    // Clicking a cell picks a shift, Leave or Off; the table is saved and redrawn straight away (?handler=Shift).
    public partial class IndexModel
    {
        [BindProperty(SupportsGet = true, Name = "view")] public string? Show { get; set; }
        [BindProperty(SupportsGet = true)] public DateOnly? Week { get; set; }
        [BindProperty(SupportsGet = true, Name = "m")] public string? MonthKey { get; set; }

        public bool IsSchedule => CurrentGroup?.Role == "Staff" && Show == "schedule";
        public ScheduleView? Schedule { get; set; }

        public record SchedShift(int Id, string Name, int Needed, double Hours, int Colour);
        public record SchedCell(int? ShiftId, bool Leave);
        public record SchedPerson(string Id, string Name, string Initials, Dictionary<DateOnly, SchedCell?> Cells, double Hours);

        public class ScheduleView
        {
            public DateOnly WeekStart { get; set; }
            public DateOnly Month { get; set; }              // first day of the month shown in the day strip
            public string MonthKey => Month.ToString("yyyy-MM");
            public DateOnly Today { get; set; }
            public List<DateOnly> Days { get; set; } = new();
            public List<SchedShift> Shifts { get; set; } = new();
            public List<SchedPerson> People { get; set; } = new();
            public HashSet<DateOnly> ShortDays { get; set; } = new();
            public DateTime? PublishedAt { get; set; }
            public bool Changed { get; set; }
            public SchedShift? ShiftOf(int? id) => id is null ? null : Shifts.FirstOrDefault(s => s.Id == id);
        }

        public static DateOnly MondayOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        public static string Hrs(double h) => h.ToString("0.#") + " h";

        public async Task LoadScheduleAsync()
        {
            var today = DateOnly.FromDateTime(KOMA.Services.DemoClock.Shared.MalaysiaNow.Date);
            var week = MondayOf(Week ?? today);
            DateOnly month;
            if (!string.IsNullOrEmpty(MonthKey) && DateOnly.TryParseExact(MonthKey + "-01", "yyyy-MM-dd", out var mk)) month = mk;
            else
            {
                var mid = Week.HasValue ? week.AddDays(3) : today;
                month = new DateOnly(mid.Year, mid.Month, 1);
            }
            Schedule = await BuildScheduleAsync(week, month, today);
        }

        private async Task<ScheduleView> BuildScheduleAsync(DateOnly week, DateOnly month, DateOnly today)
        {
            var staff = (await _userManager.GetUsersInRoleAsync("Staff"))
                .Select(u => new AccountRow(u.Id, u.Email ?? u.UserName ?? "", "Staff", u.FullName))
                .OrderBy(a => DisplayName(a), StringComparer.OrdinalIgnoreCase).ToList();
            var shifts = await _db.Shifts.AsNoTracking().Where(s => s.IsActive)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();

            var monthEnd = month.AddMonths(1).AddDays(-1);
            var from = week < month ? week : month;
            var to = week.AddDays(6) > monthEnd ? week.AddDays(6) : monthEnd;
            var ids = staff.Select(s => s.UserId).ToList();
            var rows = await _db.ShiftAssignments.AsNoTracking()
                .Where(a => a.Date >= from && a.Date <= to && ids.Contains(a.UserId)).ToListAsync();

            var v = new ScheduleView { WeekStart = week, Month = month, Today = today };
            v.Days = Enumerable.Range(0, 7).Select(i => week.AddDays(i)).ToList();
            v.Shifts = shifts.Select((s, i) => new SchedShift(s.Id, s.Name, s.Needed, s.Hours, i % 6)).ToList();

            foreach (var p in staff)
            {
                var cells = new Dictionary<DateOnly, SchedCell?>();
                double hours = 0;
                foreach (var d in v.Days)
                {
                    var r = rows.FirstOrDefault(x => x.UserId == p.UserId && x.Date == d);
                    cells[d] = r is null ? null : new SchedCell(r.IsLeave ? null : r.ShiftId, r.IsLeave);
                    if (r is { IsLeave: false }) hours += v.ShiftOf(r.ShiftId)?.Hours ?? 0;
                }
                v.People.Add(new SchedPerson(p.UserId, DisplayName(p), Initials(p), cells, hours));
            }

            // A day is short when it has been planned and a shift has fewer people than it needs.
            var planned = rows.Select(r => r.Date).ToHashSet();
            foreach (var d in planned)
                if (v.Shifts.Any(s => s.Needed > 0 && rows.Count(r => r.Date == d && !r.IsLeave && r.ShiftId == s.Id) < s.Needed))
                    v.ShortDays.Add(d);

            var w = await _db.ScheduleWeeks.AsNoTracking().FirstOrDefaultAsync(x => x.WeekStart == week);
            v.PublishedAt = w?.PublishedAt;
            v.Changed = w?.ChangedAt is not null && (w.PublishedAt is null || w.ChangedAt > w.PublishedAt);
            return v;
        }

        private async Task TouchWeekAsync(DateOnly week)
        {
            var w = await _db.ScheduleWeeks.FirstOrDefaultAsync(x => x.WeekStart == week);
            if (w is null) { w = new ScheduleWeek { WeekStart = week }; _db.ScheduleWeeks.Add(w); }
            w.ChangedAt = KOMA.Services.DemoClock.Shared.MalaysiaNow;
        }

        // One cell: value = shift id | "leave" | "off". Returns the redrawn schedule.
        public async Task<IActionResult> OnPostShiftAsync(string userId, DateOnly date, string value, string? m)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null || !await _userManager.IsInRoleAsync(user, "Staff")) return BadRequest();
            var row = await _db.ShiftAssignments.FirstOrDefaultAsync(a => a.UserId == userId && a.Date == date);
            if (value == "off")
            {
                if (row is not null) _db.ShiftAssignments.Remove(row);
            }
            else
            {
                int? shiftId = null;
                var leave = value == "leave";
                if (!leave)
                {
                    if (!int.TryParse(value, out var sid) || !await _db.Shifts.AnyAsync(s => s.Id == sid && s.IsActive)) return BadRequest();
                    shiftId = sid;
                }
                if (row is null) { row = new ShiftAssignment { UserId = userId, Date = date }; _db.ShiftAssignments.Add(row); }
                row.ShiftId = shiftId;
                row.IsLeave = leave;
            }
            await TouchWeekAsync(MondayOf(date));
            await _db.SaveChangesAsync();

            Group = "staff"; Show = "schedule"; Week = MondayOf(date); MonthKey = m;
            await LoadScheduleAsync();
            return Partial("_Schedule", Schedule!);
        }

        // Copy last week's shifts into this week (leave isn't copied). Replaces what this week had.
        public async Task<IActionResult> OnPostCopyWeekAsync(DateOnly week, string? m)
        {
            week = MondayOf(week);
            var src = week.AddDays(-7);
            var staffIds = (await _userManager.GetUsersInRoleAsync("Staff")).Select(u => u.Id).ToList();
            var source = await _db.ShiftAssignments.AsNoTracking()
                .Where(a => a.Date >= src && a.Date <= src.AddDays(6) && !a.IsLeave && staffIds.Contains(a.UserId)).ToListAsync();
            if (source.Count == 0)
            {
                TempData["StatusMessage"] = "Last week has no shifts to copy.";
            }
            else
            {
                var target = await _db.ShiftAssignments
                    .Where(a => a.Date >= week && a.Date <= week.AddDays(6) && staffIds.Contains(a.UserId)).ToListAsync();
                _db.ShiftAssignments.RemoveRange(target);
                await _db.SaveChangesAsync();
                foreach (var s in source)
                    _db.ShiftAssignments.Add(new ShiftAssignment { UserId = s.UserId, Date = s.Date.AddDays(7), ShiftId = s.ShiftId });
                await TouchWeekAsync(week);
                await _db.SaveChangesAsync();
                TempData["StatusMessage"] = "Copied last week's shifts.";
            }
            return RedirectToPage(new { group = "staff", view = "schedule", week = week.ToString("yyyy-MM-dd"), m });
        }

        // Publish = this week's schedule is final (staff will see the published version).
        public async Task<IActionResult> OnPostPublishAsync(DateOnly week, string? m)
        {
            week = MondayOf(week);
            var w = await _db.ScheduleWeeks.FirstOrDefaultAsync(x => x.WeekStart == week);
            if (w is null) { w = new ScheduleWeek { WeekStart = week, ChangedAt = KOMA.Services.DemoClock.Shared.MalaysiaNow }; _db.ScheduleWeeks.Add(w); }
            w.PublishedAt = KOMA.Services.DemoClock.Shared.MalaysiaNow;
            w.PublishedBy = User.Identity?.Name;
            await _db.SaveChangesAsync();
            TempData["StatusMessage"] = $"Schedule for {week:d MMM} – {week.AddDays(6):d MMM} published.";
            return RedirectToPage(new { group = "staff", view = "schedule", week = week.ToString("yyyy-MM-dd"), m });
        }

        // ---------- Staff panel: Schedule tab (this month for one person) ----------
        public record PanelShiftDay(DateOnly Date, string? Name, int Colour, bool Leave);

        private async Task FillPanelScheduleAsync(StaffPanel p)
        {
            try
            {
                var today = DateOnly.FromDateTime(KOMA.Services.DemoClock.Shared.MalaysiaNow.Date);
                var first = new DateOnly(today.Year, today.Month, 1);
                var last = first.AddMonths(1).AddDays(-1);
                var shifts = await _db.Shifts.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
                var colour = shifts.Select((s, i) => (s.Id, i % 6)).ToDictionary(x => x.Id, x => x.Item2);
                var rows = await _db.ShiftAssignments.AsNoTracking().Include(a => a.Shift)
                    .Where(a => a.UserId == p.Id && a.Date >= first && a.Date <= today.AddDays(62)).ToListAsync();
                p.HasSchedule = true;
                p.SchedMonth = first;
                p.SchedDays = rows.Where(r => r.Date <= last)
                    .Select(r => new PanelShiftDay(r.Date, r.IsLeave ? "Leave" : r.Shift?.Name, r.ShiftId is int id && colour.TryGetValue(id, out var c) ? c : 0, r.IsLeave)).ToList();
                var next = rows.Where(r => !r.IsLeave && r.Date > today).OrderBy(r => r.Date).FirstOrDefault();
                if (next is not null) p.NextShift = new PanelShiftDay(next.Date, next.Shift?.Name, next.ShiftId is int nid && colour.TryGetValue(nid, out var nc) ? nc : 0, false);
                var month = rows.Where(r => r.Date <= last).ToList();
                p.ShiftsMonth = month.Count(r => !r.IsLeave);
                p.HoursMonth = month.Where(r => !r.IsLeave).Sum(r => r.Shift?.Hours ?? 0);
                p.LeaveMonth = month.Count(r => r.IsLeave);
            }
            catch { p.HasSchedule = false; }   // schedule tables not created yet (migration not run)
        }
    }
}

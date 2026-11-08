using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Staff.Reservations;

[Authorize(Roles = "Staff,Administrator")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(ApplicationDbContext context) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public DateOnly? Date { get; set; }

    [BindProperty(SupportsGet = true)]
    public TimeOnly? Time { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Now { get; set; }

    public TableSelection? Selection { get; private set; }

    public IReadOnlyList<TimeOnly> Slots { get; private set; } = [];

    public List<FloorLayout> Floors { get; private set; } = [];

    public List<KOMA.Models.Reservation> Bookings { get; private set; } = [];

    public List<TableServiceSession> WalkIns { get; private set; } = [];

    public bool ValidSlot => Selection != null;

    public object PanelContext => new
    {
        Date = Date?.ToString("yyyy-MM-dd"),
        Time = Time?.ToString("HH:mm"),
        At = Selection?.SnapshotParameter
    };

    public List<KOMA.Models.Reservation> CallsDue { get; private set; } = [];
    public HashSet<int> BlockedTables { get; private set; } = [];
    public async Task OnGetAsync()
    {
        if (!ModelState.IsValid)
            return;

        var now = ReservationRules.MalaysiaNow;

        var settings = await context.RestaurantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync();

        Slots = settings == null
            ? []
            : ReservationRules.Slots(settings);

        if (Now)
        {
            Date = DateOnly.FromDateTime(now);

            Time = Slots
                .Select(slot => (TimeOnly?)slot)
                .FirstOrDefault(slot =>
                    Date.Value.ToDateTime(slot!.Value) <= now &&
                    Date.Value.ToDateTime(slot.Value)
                        .AddMinutes(ReservationRules.SlotMinutes) > now);

            // Let the controls display the current selection.
            ModelState.Remove(nameof(Date));
            ModelState.Remove(nameof(Time));
        }

        Date ??= DateOnly.FromDateTime(now);

        Time ??= Slots
            .Select(slot => (TimeOnly?)slot)
            .FirstOrDefault(slot =>
                Date.Value.ToDateTime(slot!.Value) > now)
            ?? Slots.Select(slot => (TimeOnly?)slot).FirstOrDefault();

        Selection = TableSelection.Resolve(
            settings,
            Date,
            Time,
            Now ? now : null);

        if (Selection == null)
        {
            ModelState.AddModelError(
                "",
                "Choose a valid date and opening-hours slot.");

            return;
        }

        Floors = await context.FloorLayouts
            .AsNoTracking()
            .Include(f => f.DiningAreas)
            .ThenInclude(a => a.Tables)
            .OrderBy(f => f.Level)
            .ThenBy(f => f.Name)
            .ToListAsync();

        var start = Selection.Start;
        var end = Selection.End;

        Bookings = await context.Reservations
            .AsNoTracking()
            .Where(r =>
                r.Status != ReservationStatus.Cancelled && r.Status != ReservationStatus.NoShow &&
                (Now
                    ? r.StartTime <= start && r.EndTime > start
                    : r.StartTime < end && r.EndTime > start))
            .OrderBy(r => r.StartTime)
            .ToListAsync();

        if (Now)
        {
            CallsDue = await context.Reservations.AsNoTracking().Include(r => r.RestaurantTable)
                .Where(r => r.Status == ReservationStatus.Confirmed && r.HoldUntil > now && r.ContactAttemptAt == null &&
                    r.StartTime <= now.AddMinutes(-ReservationRules.CallMinutes) &&
                    !context.TableServiceSessions.Any(s => s.ReservationId == r.Id)).ToListAsync();
            BlockedTables = (await context.Reservations.Where(r => r.Status == ReservationStatus.Confirmed &&
                r.EndTime <= now && r.EndTime > now.AddMinutes(-ReservationRules.TurnaroundMinutes))
                .Select(r => r.RestaurantTableId).Union(context.TableServiceSessions.Where(s => s.EndTime <= now &&
                    s.EndTime > now.AddMinutes(-ReservationRules.TurnaroundMinutes)).Select(s => s.RestaurantTableId)).ToListAsync()).ToHashSet();
        }
        else
        {
            var available = ReservationService.AvailableTables(context, 1, null, start, start, end).Select(t => t.Id);
            BlockedTables = (await context.RestaurantTables.Where(t => !available.Contains(t.Id)).Select(t => t.Id).ToListAsync()).ToHashSet();
        }
        WalkIns = await context.TableServiceSessions
            .AsNoTracking()
            .Where(s =>
                s.ReservationId == null &&
                (Now
                    ? s.StartTime <= start && s.EndTime > start
                    : s.StartTime < end && s.EndTime > start))
            .ToListAsync();
    }

    public async Task<IActionResult> OnGetFloorAsync()
    {
        await OnGetAsync();

        if (!ValidSlot)
        {
            return BadRequest(
                "The selected time is no longer valid. Choose a slot again.");
        }

        return Partial("_ReservationFloor", this);
    }
}

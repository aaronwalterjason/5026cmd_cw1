using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Staff.Reservations;

[Authorize(Roles = "Staff,Administrator")]
public class IndexModel(ApplicationDbContext context) : PageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    [BindProperty(SupportsGet = true)] public TimeOnly? Time { get; set; }
    public IReadOnlyList<TimeOnly> Slots { get; set; } = [];
    public List<RestaurantTable> Tables { get; set; } = [];
    public List<FloorLayout> Floors { get; set; } = [];
    public List<KOMA.Models.Reservation> Bookings { get; set; } = [];
    public bool ValidSlot { get; set; }
    public bool FutureSlot { get; set; }

    public async Task OnGetAsync()
    {
        Date ??= DateOnly.FromDateTime(ReservationRules.MalaysiaNow);
        var settings = await context.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync();
        if (settings != null) Slots = ReservationRules.Slots(settings);
        Time ??= Slots.Cast<TimeOnly?>().FirstOrDefault(t => Date.Value.ToDateTime(t!.Value) > ReservationRules.MalaysiaNow)
            ?? Slots.Cast<TimeOnly?>().FirstOrDefault();
        ValidSlot = ModelState.IsValid && Date != DateOnly.MaxValue && Time.HasValue && Slots.Contains(Time.Value);
        if (!ValidSlot) { ModelState.AddModelError("", "Choose a valid date and an available opening-hours slot."); return; }
        var start = Date.Value.ToDateTime(Time!.Value);
        var end = start.AddMinutes(ReservationRules.SlotMinutes);
        FutureSlot = start > ReservationRules.MalaysiaNow;
        Floors = await context.FloorLayouts
            .AsNoTracking()
            .Include(f => f.DiningAreas)
                .ThenInclude(a => a.Tables)
            .OrderBy(f => f.Level)
            .ThenBy(f => f.Name)
            .ToListAsync();
        Tables = await context.RestaurantTables.AsNoTracking().Include(t => t.DiningArea).ThenInclude(a => a!.FloorLayout)
            .Where(t => t.IsActive && t.DiningArea!.IsActive).OrderBy(t => t.DiningAreaId).ThenBy(t => t.TableNumber).ToListAsync();
        // Include inactive tables' reservations in the list to preserve access to their bookings.
        Bookings = await context.Reservations.AsNoTracking().Include(r => r.RestaurantTable)
            .Where(r => r.StartTime < end && r.EndTime > start).OrderBy(r => r.StartTime).ThenBy(r => r.Id).ToListAsync();
    }
}

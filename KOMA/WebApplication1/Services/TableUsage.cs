using KOMA.Data;
using KOMA.Models;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Services;

// What a table is used for, so the admin pages can decide what is safe:
//   Upcoming bookings  -> can't delete or archive it; move the bookings first (ReservationService.MoveOffTableAsync)
//   Guests seated now  -> can't delete or archive it
//   Any history        -> can't delete it (bookings, bills and reports point at it); it is ARCHIVED instead
//   Nothing            -> can be deleted
// Used by Admin > Tables (Delete/Edit), Dining areas (Delete/Edit), Floor layouts (Delete) and the floor designer.
public static class TableUsage
{
    public record Info(int TableId, string Number, int Upcoming, DateTime? Next, int MaxGuests, bool Seated, bool HasHistory)
    {
        public bool CanDelete => !HasHistory && Upcoming == 0 && !Seated;
        public bool CanArchive => Upcoming == 0 && !Seated;
        public string NextText => Next is { } n ? n.ToString("ddd d MMM, h:mm tt") : "";
    }

    // Booking statuses that still need their table.
    private static bool Live(ReservationStatus s) => s is ReservationStatus.Pending or ReservationStatus.Confirmed;

    public static async Task<Dictionary<int, Info>> ForTablesAsync(ApplicationDbContext db, IEnumerable<int> tableIds)
    {
        var ids = tableIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var now = ReservationRules.MalaysiaNow;

        var numbers = await db.RestaurantTables.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.TableNumber }).ToDictionaryAsync(t => t.Id, t => t.TableNumber);
        var bookings = await db.Reservations.AsNoTracking().Where(r => ids.Contains(r.RestaurantTableId))
            .Select(r => new { r.RestaurantTableId, r.Status, r.StartTime, r.EndTime, r.GuestCount }).ToListAsync();
        var visits = await db.TableServiceSessions.AsNoTracking().Where(s => ids.Contains(s.RestaurantTableId))
            .Select(s => new { s.RestaurantTableId, s.StartTime, s.EndTime }).ToListAsync();
        var ordered = (await db.Orders.AsNoTracking().Where(o => o.RestaurantTableId != null && ids.Contains(o.RestaurantTableId.Value))
            .Select(o => o.RestaurantTableId!.Value).Distinct().ToListAsync()).ToHashSet();

        var result = new Dictionary<int, Info>();
        foreach (var id in ids)
        {
            var upcoming = bookings.Where(b => b.RestaurantTableId == id && Live(b.Status) && b.EndTime > now).OrderBy(b => b.StartTime).ToList();
            var seated = visits.Any(v => v.RestaurantTableId == id && v.StartTime <= now && v.EndTime > now);
            var history = bookings.Any(b => b.RestaurantTableId == id) || visits.Any(v => v.RestaurantTableId == id) || ordered.Contains(id);
            result[id] = new Info(id, numbers.GetValueOrDefault(id, "?"), upcoming.Count, upcoming.FirstOrDefault()?.StartTime,
                upcoming.Count == 0 ? 0 : upcoming.Max(b => b.GuestCount), seated, history);
        }
        return result;
    }

    // Every table in these dining areas, archived ones included (a dining area can't be deleted while any has history).
    public static async Task<Dictionary<int, Info>> ForAreasAsync(ApplicationDbContext db, IEnumerable<int> areaIds)
    {
        var ids = areaIds.ToList();
        var tableIds = await db.RestaurantTables.Where(t => ids.Contains(t.DiningAreaId)).Select(t => t.Id).ToListAsync();
        return await ForTablesAsync(db, tableIds);
    }

    // "Table T4 has 2 upcoming bookings (next: Fri 28 Nov, 7:00 PM)"
    public static string UpcomingText(Info i) =>
        $"Table {i.Number} has {i.Upcoming} upcoming booking{(i.Upcoming == 1 ? "" : "s")} (next: {i.NextText})";

    // Why a table can't be removed right now, or null if it can be deleted or archived.
    public static string? BlockReason(Info i) =>
        i.Seated ? $"Guests are seated at table {i.Number} right now."
        : i.Upcoming > 0 ? UpcomingText(i) + ". Move the bookings first."
        : null;

    // Archive: kept for history (bookings, bills, reports) but hidden everywhere and never bookable.
    // Its number can be reused by a new table.
    public static void Archive(RestaurantTable t)
    {
        t.IsArchived = true;
        t.IsActive = false;
        t.TabletDeviceId = null;
    }
}

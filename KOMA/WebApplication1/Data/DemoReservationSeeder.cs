using KOMA.Models;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Data;

// Development only: fills the database with realistic sample bookings (the last 14 days and the next 7 days)
// so the staff Reservations map, My Reservations and Admin > Activity have something to show.
// Runs once - it skips if demo bookings already exist (marked with CreatedByUserId = "demo-seed").
// Follows the booking rules: one-hour slots inside opening hours, guests <= table seats, no double-booked tables.
public static class DemoReservationSeeder
{
    public const string Marker = "demo-seed";

    private static readonly string[] PhoneNames =
    {
        "Aisyah Rahman", "Daniel Wong", "Kavitha Raj", "Marcus Lee", "Nurul Huda", "Jason Tan",
        "Priya Menon", "Farid Ismail", "Chloe Lim", "Arjun Pillai", "Mei Chen", "Hafiz Zulkifli"
    };
    private static readonly string[] Requests =
    {
        "Birthday - cake at dessert time", "Window seat if possible", "Baby chair needed", "Anniversary dinner",
        "One guest is vegetarian", "Wheelchair access", "Quiet table please", "Business lunch - need to leave by 2 PM"
    };

    public static async Task SeedAsync(ApplicationDbContext db)
    {
        if (await db.Reservations.AnyAsync(r => r.CreatedByUserId == Marker)) return;

        var tables = await db.RestaurantTables.AsNoTracking()
            .Where(t => t.IsActive && t.DiningArea!.IsActive)
            .ToListAsync();
        if (tables.Count == 0) return;   // draw tables in Admin > Floor layouts first

        var settings = await db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync() ?? new RestaurantSettings();
        var slots = Services.ReservationRules.Slots(settings).ToList();
        if (slots.Count == 0) return;

        var emails = DemoOrderSeeder.DemoCustomers.Select(c => c.Email).Append("customer@koma.local").ToList();
        var customers = await db.Users.AsNoTracking()
            .Where(u => emails.Contains(u.Email!))
            .Select(u => new { u.Id, Name = u.FullName ?? u.Email!, u.Email, u.PhoneNumber })
            .ToListAsync();

        // Busy times get picked more often (lunch 12-1 PM, dinner 6-8 PM).
        var weighted = slots.SelectMany(s => Enumerable.Repeat(s, s.Hour is 12 or 13 ? 3 : s.Hour is >= 18 and <= 20 ? 4 : 1)).ToList();

        var rng = new Random(2026);
        var now = Services.ReservationRules.MalaysiaNow;
        // Slots already booked by real reservations, so demo bookings never clash with them.
        var existing = await db.Reservations.Where(r => r.Status != ReservationStatus.Cancelled)
            .Select(r => new { r.RestaurantTableId, r.StartTime }).ToListAsync();
        var taken = existing.Select(x => (Table: x.RestaurantTableId, Start: x.StartTime)).ToHashSet();

        var list = new List<Reservation>();
        for (var dayOffset = -14; dayOffset <= 7; dayOffset++)
        {
            var date = now.Date.AddDays(dayOffset);
            var weekend = date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday;
            var count = rng.Next(weekend ? 7 : 4, weekend ? 12 : 8);

            for (var i = 0; i < count; i++)
            {
                var slot = weighted[rng.Next(weighted.Count)];
                var start = date.Add(slot.ToTimeSpan());
                var guests = rng.Next(100) switch { < 35 => 2, < 60 => 4, < 75 => 3, < 88 => 6, < 95 => 5, _ => 8 };

                // A free table that is big enough (smallest fitting table first, like a host would choose).
                var table = tables.Where(t => t.Capacity >= guests && !taken.Contains((t.Id, start)))
                                  .OrderBy(t => t.Capacity).ThenBy(_ => rng.Next()).FirstOrDefault();
                if (table is null) continue;

                var online = customers.Count > 0 && rng.Next(100) < 60;
                var c = online ? customers[rng.Next(customers.Count)] : null;
                var name = c?.Name ?? PhoneNames[rng.Next(PhoneNames.Length)];

                var past = start < now;
                var status = rng.Next(100) switch
                {
                    < 9 => ReservationStatus.Cancelled,
                    < 22 when !past => ReservationStatus.Pending,
                    _ => ReservationStatus.Confirmed
                };

                list.Add(new Reservation
                {
                    RestaurantTableId = table.Id,
                    CustomerUserId = c?.Id,
                    CustomerName = name,
                    ContactNumber = c?.PhoneNumber ?? $"01{rng.Next(0, 10)}-{rng.Next(200, 999)} {rng.Next(1000, 9999)}",
                    Email = c?.Email,
                    StartTime = start,
                    EndTime = start.AddMinutes(Services.ReservationRules.SlotMinutes),
                    GuestCount = guests,
                    SpecialRequests = rng.Next(100) < 25 ? Requests[rng.Next(Requests.Length)] : null,
                    Status = status,
                    CreatedAtUtc = start.AddDays(-rng.Next(1, 6)).AddHours(-rng.Next(1, 10)).AddHours(-8),
                    CreatedByUserId = Marker,   // marks demo data (staff/customer who "made" it is implied by CustomerUserId)
                });
                if (status != ReservationStatus.Cancelled) taken.Add((table.Id, start));
            }
        }
        db.Reservations.AddRange(list);
        await db.SaveChangesAsync();
    }
}

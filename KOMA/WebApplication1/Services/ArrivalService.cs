using KOMA.Data;
using KOMA.Models;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Services;

public class ArrivalService(ApplicationDbContext db, DiningOrderService dining, Email.BookingEmailService emails)
{
    // Caller holds the reservation write lock. A linked dining session records check-in.
    internal static async Task ExpireAsync(ApplicationDbContext db, DateTime now)
    {
        var expired = db.Reservations.Where(r => r.Status == ReservationStatus.Confirmed &&
            r.HoldUntil <= now && !db.TableServiceSessions.Any(s => s.ReservationId == r.Id));
        await db.Orders.Where(o => o.Status == OrderStatus.AwaitingArrival &&
            expired.Any(r => r.Id == o.ReservationId)).ExecuteUpdateAsync(u => u.SetProperty(o => o.Status, OrderStatus.Cancelled));
        await expired.ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, ReservationStatus.NoShow));
    }

    public async Task SweepAsync()
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await dining.LockAsync();
        await ExpireAsync(db, ReservationRules.MalaysiaNow);
        await tx.CommitAsync();
    }

    public async Task<string?> UpdateAsync(int id, int table, string version, string action, TimeOnly? eta, int? destination)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await dining.LockAsync();
        var now = ReservationRules.MalaysiaNow;
        var r = await db.Reservations.SingleOrDefaultAsync(r => r.Id == id);
        if (r == null || r.RestaurantTableId != table || r.Status != ReservationStatus.Confirmed ||
            r.StartTime > now || r.HoldUntil == null || r.HoldUntil <= now || Convert.ToBase64String(r.RowVersion) != version ||
            await db.TableServiceSessions.AnyAsync(s => s.ReservationId == id))
            return "The booking or hold has changed. Refresh Table service.";
        switch (action)
        {
            case "arrived":
                await DiningOrderService.BookingSessionAsync(db, r);
                break;
            case "called":
                r.ContactAttemptAt = now;
                break;
            case "cancel":
                r.Status = ReservationStatus.Cancelled;
                await db.Orders.Where(o => o.ReservationId == id && o.Status == OrderStatus.AwaitingArrival)
                    .ExecuteUpdateAsync(u => u.SetProperty(o => o.Status, OrderStatus.Cancelled));
                await emails.QueueCancelledAsync(r, byCustomer: false);
                break;
            case "eta" when eta.HasValue:
                var arrival = DateOnly.FromDateTime(now).ToDateTime(eta.Value);
                var end = arrival.AddMinutes(ReservationRules.SlotMinutes);
                var ready = end.AddMinutes(ReservationRules.TurnaroundMinutes);
                var settings = await db.RestaurantSettings.FirstAsync();
                if (arrival <= now || ready.Date != now.Date || TimeOnly.FromDateTime(ready) > settings.ClosingTime)
                    return "Choose a future arrival today with time for dining and turnaround before closing.";
                var choices = await ReservationService.AvailableTables(db, r.GuestCount, id, r.StartTime, now, end)
                    .OrderBy(t => t.TableNumber).Select(t => new { t.Id, t.TableNumber }).ToListAsync();
                var target = destination ?? table;
                if (!choices.Any(t => t.Id == target))
                    return choices.Count == 0 ? "No suitable table is free. Contact the guest and cancel the reservation." :
                        "That table is unavailable. Offer one of these tables: " + string.Join(", ", choices.Select(t => t.TableNumber));
                r.RestaurantTableId = target;
                r.ContactAttemptAt = now;
                r.ExpectedArrival = arrival;
                r.HoldUntil = arrival;
                r.EndTime = end;
                await db.Orders.Where(o => o.ReservationId == id).ExecuteUpdateAsync(u => u.SetProperty(o => o.RestaurantTableId, (int?)target));
                break;
            default: return "Choose an arrival action.";
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return null;
    }
}

public sealed class ArrivalWorker(IServiceScopeFactory scopes, ILogger<ArrivalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ArrivalService>().SweepAsync();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Could not release overdue reservations.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

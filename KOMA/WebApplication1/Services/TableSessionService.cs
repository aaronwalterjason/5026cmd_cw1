using KOMA.Data;
using KOMA.Models;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Services;

public class TableSessionService(ApplicationDbContext db)
{
    // Use the same lock as ReservationService to prevent
    // simultaneous bookings and walk-ins taking the same table.
    private Task LockAsync() =>
        db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = 'KOMA.ReservationWrites',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;

            IF @result < 0
                THROW 51000, 'Table service is busy. Please retry.', 1;
            """);

    public async Task<(int? Id, string? Error)> CreateWalkInAsync(
        int tableId,
        string name,
        string? phone,
        int guests,
        int minutes)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Trim().Length > 100 ||
            phone?.Length > 30 ||
            guests < 1 ||
            guests > 20 ||
            minutes < 15 ||
            minutes > 240)
        {
            return (
                null,
                "Enter a name, 1–20 guests and a visit duration of 15–240 minutes."
            );
        }

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        await LockAsync();

        var now = ReservationRules.MalaysiaNow;
        var end = now.AddMinutes(minutes);

        var settings = await db.RestaurantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync();

        var table = await db.RestaurantTables
            .Include(t => t.DiningArea)
            .SingleOrDefaultAsync(t => t.Id == tableId);

        if (table == null ||
            !table.IsActive ||
            table.DiningArea?.IsActive != true ||
            guests > table.Capacity)
        {
            return (null, "Choose an active table with enough seats.");
        }

        if (settings == null ||
            TimeOnly.FromDateTime(now) < settings.OpeningTime ||
            end.Date != now.Date ||
            TimeOnly.FromDateTime(end) > settings.ClosingTime)
        {
            return (
                null,
                "The walk-in visit must fit within today's opening hours."
            );
        }

        var overlapsBooking = await db.Reservations.AnyAsync(r =>
            r.RestaurantTableId == tableId &&
            r.Status != ReservationStatus.Cancelled &&
            r.StartTime < end &&
            r.EndTime > now);

        var overlapsWalkIn = await db.TableServiceSessions.AnyAsync(s =>
            s.RestaurantTableId == tableId &&
            s.ReservationId == null &&
            s.StartTime < end &&
            s.EndTime > now);

        if (overlapsBooking || overlapsWalkIn)
        {
            return (
                null,
                "This table has a booking or walk-in during that time. " +
                "Choose a shorter duration or another table."
            );
        }

        var session = new TableServiceSession
        {
            RestaurantTableId = tableId,
            CustomerName = name.Trim(),
            ContactNumber = phone?.Trim(),
            GuestCount = guests,
            StartTime = now,
            EndTime = end
        };

        db.TableServiceSessions.Add(session);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return (session.Id, null);
    }

    // Staff must review an existing order before assigning it.
    // Do not guess the visit from the table number or order date.
    public async Task<string?> LinkOrderAsync(
        int tableId,
        int? bookingId,
        int? sessionId,
        int orderId)
    {
        if (bookingId.HasValue && sessionId.HasValue)
        {
            return "Select either a booking or a walk-in visit.";
        }

        await using var transaction =
            await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

        await LockAsync();

        var order = await db.Orders
            .SingleOrDefaultAsync(o => o.Id == orderId);

        if (order == null ||
            order.RestaurantTableId != tableId ||
            order.TableServiceSessionId != null)
        {
            return "This order is missing, belongs to another table, " +
                   "or is already linked. Refresh and check it.";
        }

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            return "A closed order cannot be assigned to a visit.";
        }

        TableServiceSession? session;

        if (bookingId.HasValue)
        {
            var booking = await db.Reservations.SingleOrDefaultAsync(r =>
                r.Id == bookingId.Value &&
                r.RestaurantTableId == tableId);

            if (booking == null ||
                booking.Status == ReservationStatus.Cancelled)
            {
                return "The booking is no longer available.";
            }

            if (booking.CustomerUserId != null &&
                order.CustomerId != null &&
                booking.CustomerUserId != order.CustomerId)
            {
                return "This order belongs to a different customer account.";
            }

            session = await db.TableServiceSessions
                .SingleOrDefaultAsync(s => s.ReservationId == booking.Id);

            if (session != null &&
                session.RestaurantTableId != tableId)
            {
                return "The linked visit belongs to another table.";
            }

            if (session == null)
            {
                session = new TableServiceSession
                {
                    ReservationId = booking.Id,
                    RestaurantTableId = tableId,
                    CustomerName = booking.CustomerName,
                    ContactNumber = booking.ContactNumber,
                    GuestCount = booking.GuestCount,
                    StartTime = booking.StartTime,
                    EndTime = booking.EndTime
                };

                db.TableServiceSessions.Add(session);
            }
        }
        else
        {
            session = await db.TableServiceSessions
                .SingleOrDefaultAsync(s =>
                    s.Id == sessionId &&
                    s.ReservationId == null &&
                    s.RestaurantTableId == tableId);

            if (session == null)
            {
                return "Select a valid walk-in visit.";
            }
        }

        order.TableServiceSession = session;

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return null;
    }
}
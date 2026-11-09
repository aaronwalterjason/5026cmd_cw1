using System.Data;
using KOMA.Data;
using KOMA.Models;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Services;

public class DiningOrderService(ApplicationDbContext db)
{
    public IQueryable<TableServiceSession> ActiveVisits()
    {
        var now = ReservationRules.MalaysiaNow;
        return db.TableServiceSessions.Where(s => s.StartTime <= now && now < s.EndTime &&
            s.RestaurantTable.IsActive && s.RestaurantTable.DiningArea!.IsActive &&
            (s.ReservationId == null || (s.Reservation!.Status == ReservationStatus.Confirmed &&
            s.Reservation.RestaurantTableId == s.RestaurantTableId)));
    }

    public IQueryable<MenuItem> Menu() => db.MenuItems.Include(m => m.Promotion).Where(m => m.IsAvailable && m.MenuCategory!.IsActive);   // Promotion: for MenuPricing

    public async Task<int> LatestOrderAsync(int visitId) => await db.Orders.Where(o => o.TableServiceSessionId == visitId)
        .Select(o => (int?)o.Id).MaxAsync() ?? 0;

    public async Task<(int? Id, string? Error)> BeginAsync(int tableId, string? customer)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockAsync();
        var now = ReservationRules.MalaysiaNow;
        if (!await db.RestaurantTables.AnyAsync(t => t.Id == tableId && t.IsActive && t.DiningArea!.IsActive))
            return (null, "Ask Staff to pair this tablet again.");
        await ArrivalService.ExpireAsync(db, now);
        var bookings = await db.Reservations.Where(r => r.Status != ReservationStatus.Cancelled && r.Status != ReservationStatus.NoShow &&
            r.StartTime <= now && now < r.EndTime && (r.RestaurantTableId == tableId ||
            (customer != null && r.CustomerUserId == customer))).ToListAsync();
        var booking = bookings.FirstOrDefault(r => customer != null && r.CustomerUserId == customer && r.RestaurantTableId == tableId && r.Status == ReservationStatus.Confirmed);
        if (booking == null && bookings.Count > 0)
            return (null, "This table or reservation does not match. Check your table number and scan your account QR.");
        TableServiceSession visit;
        if (booking != null) visit = await BookingSessionAsync(db, booking);
        else
        {
            if (await ActiveVisits().AnyAsync(s => s.RestaurantTableId == tableId))
                return (null, "This table has an active visit. Finish the current visit before starting another.");
            var next = await db.Reservations.Where(r => r.RestaurantTableId == tableId && r.Status != ReservationStatus.Cancelled && r.Status != ReservationStatus.NoShow && r.StartTime > now)
                .Select(r => (DateTime?)r.StartTime).MinAsync();
            var end = now.AddMinutes(ReservationRules.SlotMinutes);
            if (next.HasValue && next.Value.AddMinutes(-ReservationRules.TurnaroundMinutes) < end)
                end = next.Value.AddMinutes(-ReservationRules.TurnaroundMinutes);
            if (end <= now || await db.TableServiceSessions.AnyAsync(s => s.RestaurantTableId == tableId && s.EndTime <= now && s.EndTime > now.AddMinutes(-ReservationRules.TurnaroundMinutes)) || await db.Reservations.AnyAsync(r => r.RestaurantTableId == tableId &&
                r.Status == ReservationStatus.Confirmed && r.EndTime <= now && r.EndTime > now.AddMinutes(-ReservationRules.TurnaroundMinutes)))
                return (null, "This table is being prepared. Ask staff for another table.");
            var name = customer == null ? "Guest" : await db.Users.Where(u => u.Id == customer).Select(u => u.FullName).SingleAsync();
            visit = new TableServiceSession { RestaurantTableId = tableId, CustomerName = string.IsNullOrWhiteSpace(name) ? "Guest" : name,
                StartTime = now, EndTime = end, GuestCount = 1 };
            db.TableServiceSessions.Add(visit);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (visit.Id, null);
    }

    public async Task EndAsync(int visitId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockAsync();
        var now = ReservationRules.MalaysiaNow;
        await db.TableServiceSessions.Where(s => s.Id == visitId && s.ReservationId == null && s.EndTime > now && !db.Orders.Any(o => o.TableServiceSessionId == visitId))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.EndTime, now));
        await tx.CommitAsync();
    }
    // Called inside the reservation write transaction.
    internal static async Task<TableServiceSession> BookingSessionAsync(ApplicationDbContext db, Reservation booking)
    {
        var visit = await db.TableServiceSessions.SingleOrDefaultAsync(s => s.ReservationId == booking.Id);
        if (visit == null)
        {
            visit = new TableServiceSession { ReservationId = booking.Id, RestaurantTableId = booking.RestaurantTableId,
                CustomerName = booking.CustomerName, ContactNumber = booking.ContactNumber, GuestCount = booking.GuestCount,
                StartTime = booking.StartTime, EndTime = booking.EndTime };
            db.TableServiceSessions.Add(visit);

        }
        return visit;
    }

    public async Task<(int? Id, string? Error)> PlaceAsync(int visitId, int expectedTable, Dictionary<int,int> quantities,
        decimal expectedTotal, int latestOrder, string? review, string? customer)
    {
        if (quantities.Any(q => q.Value < 0 || q.Value > 99))
            return (null, "Choose up to 99 of each dish.");
        var selected = quantities.Where(q => q.Value > 0).ToDictionary(q => q.Key, q => q.Value);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockAsync();
        var visit = await ActiveVisits().Include(s => s.Reservation).SingleOrDefaultAsync(s => s.Id == visitId && s.RestaurantTableId == expectedTable);
        if (visit == null) return (null, "This dining visit has ended. Ask Staff to check the table.");
        var preorder = visit.ReservationId.HasValue ? await db.Orders.Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.ReservationId == visit.ReservationId && o.Status == OrderStatus.AwaitingArrival) : null;
        if (preorder != null && (preorder.CustomerId != customer || preorder.RestaurantTableId != visit.RestaurantTableId ||
            preorder.Items.Count == 0 || preorder.PaymentStatus == PaymentStatus.Refunded))
            return (null, "The pre-order has changed. Ask Staff to check this booking.");
        if ((preorder == null ? "" : OrderWorkflowService.ReviewToken(preorder)) != (review ?? ""))
            return (null, "Your pre-order changed. Review the updated bill before confirming.");
        if (selected.Count == 0 && preorder == null) return (null, "Choose at least one dish.");
        if (await LatestOrderAsync(visitId) != latestOrder)
            return (null, "Another order was placed for this visit. Review the latest orders before sending again.");
        var ids = selected.Keys.ToArray();
        var dishes = await Menu().Where(m => ids.Contains(m.Id)).ToListAsync();
        if (dishes.Count != selected.Count) return (null, "A selected dish is no longer available. Review the menu.");
        var lines = OrderingMenu.BuildLines(dishes, selected, ReservationRules.MalaysiaNow);   // tablet: promotions running now
        var total = lines.Sum(i => i.LineTotal);
        if (total + (preorder?.TotalAmount ?? 0) != expectedTotal || total > 99999999.99m)
            return (null, "Review the current prices and total before sending.");
        if (preorder != null)
        {
            preorder.TableServiceSessionId = visit.Id;
            preorder.Status = OrderStatus.QueuedForKitchen;
            if (selected.Count == 0)
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return (preorder.Id, null);
            }
        }
        var order = new Order { TableServiceSessionId = visit.Id, RestaurantTableId = visit.RestaurantTableId,
            CustomerName = visit.CustomerName, CustomerId = customer,
            OrderType = customer != null ? OrderType.TableTablet : OrderType.WalkIn,
            OrderedAt = ReservationRules.MalaysiaNow, Status = OrderStatus.QueuedForKitchen,
            TotalAmount = total };
        foreach (var line in lines) order.Items.Add(line);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (order.Id, null);
    }

    internal Task LockAsync() => db.Database.ExecuteSqlRawAsync("""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource = 'KOMA.ReservationWrites',
            @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @result < 0 THROW 51000, 'Dining service is busy. Please retry.', 1;
        """);
}

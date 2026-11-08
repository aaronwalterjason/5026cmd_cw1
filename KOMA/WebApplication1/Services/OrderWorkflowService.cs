using System.Data;
using KOMA.Data;
using KOMA.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace KOMA.Services;

public class OrderWorkflowService(ApplicationDbContext db)
{
    public static readonly OrderStatus[] Steps =
    [
        OrderStatus.Pending,
        OrderStatus.QueuedForKitchen,
        OrderStatus.Preparing,
        OrderStatus.ReadyToServe,
        OrderStatus.Served,
        OrderStatus.Completed
    ];

    public static OrderStatus? Next(Order order) => order.Status switch
    {
        OrderStatus.AwaitingArrival => OrderStatus.QueuedForKitchen,
        OrderStatus.Pending => OrderStatus.QueuedForKitchen,
        OrderStatus.QueuedForKitchen => OrderStatus.Preparing,
        OrderStatus.Preparing => OrderStatus.ReadyToServe,
        OrderStatus.ReadyToServe => OrderStatus.Served,

        OrderStatus.Served
            when order.PaymentStatus == PaymentStatus.Paid
            => OrderStatus.Completed,

        _ => null
    };

    public static string ActionLabel(OrderStatus status) => status switch
    {
        OrderStatus.AwaitingArrival => "Send to kitchen",
        OrderStatus.Pending => "Send to kitchen",
        OrderStatus.QueuedForKitchen => "Start preparing",
        OrderStatus.Preparing => "Mark ready",
        OrderStatus.ReadyToServe => "Mark served",
        OrderStatus.Served => "Complete order",
        _ => "Update order"
    };

    public static string ReviewToken(Order order)
    {
        var snapshot = new
        {
            order.Id,
            order.ReservationId,
            order.TableServiceSessionId,
            order.RestaurantTableId,
            order.CustomerId,
            order.CustomerName,
            order.Status,
            order.PaymentStatus,
            order.TotalAmount,
            order.Notes,
            Items = order.Items
                .OrderBy(i => i.Id)
                .Select(i => new
                {
                    i.Id,
                    i.MenuItemId,
                    i.ItemName,
                    i.Quantity,
                    i.UnitPrice
                })
                .ToArray()
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public async Task<string?> AdvanceAsync(
        int id,
        OrderStatus expected,
        int? expectedSessionId = null,
        int? expectedBookingId = null,
        int? expectedTableId = null,
        string? expectedReview = null,
        bool arrivalConfirmed = false)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

        // Coordinate with customer pre-order edits and reservation changes.
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = 'KOMA.ReservationWrites',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;

            IF @result < 0
                THROW 51000, 'Order service is busy. Please retry.', 1;
            """);

        var order = await db.Orders
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Id == id);

        if (order == null)
            return "This order no longer exists.";

        if (order.Status != expected)
        {
            return "Another user already updated this order. " +
                   "Close and reopen the table to review its current status.";
        }

        if (order.PaymentStatus == PaymentStatus.Refunded)
            return "A refunded order cannot be sent to the kitchen or advanced.";

        if (expectedTableId.HasValue &&
            order.RestaurantTableId != expectedTableId.Value)
        {
            return "This order belongs to another table. Refresh the table.";
        }

        if (expectedBookingId.HasValue)
        {
            // A pre-order can use its direct reservation link.
            var belongsToBooking =
                order.ReservationId == expectedBookingId.Value;

            // Older manually linked orders may use the visit link instead.
            if (!order.ReservationId.HasValue &&
                order.TableServiceSessionId.HasValue)
            {
                belongsToBooking = await db.TableServiceSessions.AnyAsync(s =>
                    s.Id == order.TableServiceSessionId.Value &&
                    s.ReservationId == expectedBookingId.Value &&
                    s.RestaurantTableId == order.RestaurantTableId);
            }

            if (!belongsToBooking)
                return "This order does not belong to the selected booking.";
        }
        else if (expectedSessionId.HasValue)
        {
            if (order.TableServiceSessionId != expectedSessionId.Value ||
                order.ReservationId.HasValue)
            {
                return "This order does not belong to the selected walk-in visit.";
            }
        }

        // Validate direct reservation links.
        if (order.ReservationId.HasValue)
        {
            var booking = await db.Reservations
                .SingleOrDefaultAsync(r => r.Id == order.ReservationId.Value);

            if (booking == null ||
                booking.Status == ReservationStatus.Cancelled)
            {
                return "This order belongs to a missing or cancelled reservation.";
            }

            if (booking.RestaurantTableId != order.RestaurantTableId)
                return "The booking's table has changed. Refresh before continuing.";

            if (expected == OrderStatus.AwaitingArrival &&
                booking.Status != ReservationStatus.Confirmed)
            {
                return "The reservation must be confirmed before sending its food.";
            }
        }

        // Also validate orders using a visit link.
        if (order.TableServiceSessionId.HasValue)
        {
            var session = await db.TableServiceSessions
                .Include(s => s.Reservation)
                .SingleOrDefaultAsync(s =>
                    s.Id == order.TableServiceSessionId.Value);

            if (session == null ||
                session.RestaurantTableId != order.RestaurantTableId)
            {
                return "The order's table visit has changed. Refresh the table.";
            }

            if (session.Reservation?.Status == ReservationStatus.Cancelled)
                return "This order belongs to a cancelled reservation.";

            if (order.ReservationId.HasValue &&
                session.ReservationId != order.ReservationId)
            {
                return "The order has inconsistent booking links. " +
                       "Ask the administrator to review it.";
            }
        }

        if (!string.IsNullOrWhiteSpace(expectedReview) &&
            expectedReview != ReviewToken(order))
        {
            return "The customer or another user changed this order. " +
                   "Close and reopen the table, review the latest dishes, " +
                   "then try again.";
        }

        if (expected == OrderStatus.AwaitingArrival)
        {
            if (!order.ReservationId.HasValue)
                return "This pre-order has no reservation link.";

            if (string.IsNullOrWhiteSpace(expectedReview))
                return "Open this pre-order from Table service before sending it.";

            if (!arrivalConfirmed)
                return "Confirm that the customer has arrived before sending food.";
        }

        var next = Next(order);

        if (next == null)
            return "This order cannot advance from its current status.";

        var sendingToKitchen =
            expected is OrderStatus.Pending or OrderStatus.AwaitingArrival;

        if (sendingToKitchen &&
            (order.Items.Count == 0 ||
             order.Items.Any(i =>
                 i.Quantity < 1 ||
                 i.Quantity > 99 ||
                 i.UnitPrice < 0)))
        {
            return "The order must contain valid dishes, quantities and prices.";
        }

        if (expected == OrderStatus.AwaitingArrival && !order.TableServiceSessionId.HasValue)
        {
            var booking = await db.Reservations.SingleAsync(r => r.Id == order.ReservationId);
            if (booking.HoldUntil <= ReservationRules.MalaysiaNow && !await db.TableServiceSessions.AnyAsync(s => s.ReservationId == booking.Id)) return "The arrival deadline has passed.";
            order.TableServiceSession = await DiningOrderService.BookingSessionAsync(db, booking);
        }
        order.Status = next.Value;

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return null;
    }

    public async Task<string?> PayAsync(
        int id,
        PaymentMethod method)
    {
        if (!Enum.IsDefined(method))
        {
            return "Choose a valid payment method.";
        }

        await using var transaction =
            await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

        var order = await db.Orders
            .SingleOrDefaultAsync(o => o.Id == id);

        if (order is null)
        {
            return "This order no longer exists.";
        }

        if (order.PaymentStatus != PaymentStatus.Unpaid ||
            order.Status == OrderStatus.Cancelled)
        {
            return "This order has already been paid or cancelled. "
                + "Refresh its details.";
        }

        RecordPayment(order, method);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return null;
    }

    internal static void RecordPayment(Order order, PaymentMethod method)
    {
        order.PaymentStatus = PaymentStatus.Paid;
        order.PaymentMethod = method;
        order.PaidAt = ReservationRules.MalaysiaNow;

        if (order.Status == OrderStatus.Served)
        {
            order.Status = OrderStatus.Completed;
        }
    }

    public async Task<string?> CancelAsync(
        int id,
        OrderStatus expected)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

        var order = await db.Orders
            .SingleOrDefaultAsync(o => o.Id == id);

        if (order is null)
        {
            return "This order no longer exists.";
        }

        if (order.Status != expected)
        {
            return "The order has changed. Review it before cancelling.";
        }

        if (order.Status is OrderStatus.Completed
            or OrderStatus.Cancelled)
        {
            return "This order is already closed.";
        }

        // Retains the existing Admin bookkeeping behaviour.
        // This does not issue a bank/payment-gateway refund.
        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            order.PaymentStatus = PaymentStatus.Refunded;
        }

        order.Status = OrderStatus.Cancelled;

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return null;
    }
}

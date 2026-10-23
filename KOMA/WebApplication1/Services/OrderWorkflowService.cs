using System.Data;
using KOMA.Data;
using KOMA.Models;
using Microsoft.EntityFrameworkCore;

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
        OrderStatus.Pending => "Confirm & send to kitchen",
        OrderStatus.QueuedForKitchen => "Start preparing",
        OrderStatus.Preparing => "Mark ready",
        OrderStatus.ReadyToServe => "Mark served",
        OrderStatus.Served => "Complete order",
        _ => "Update order"
    };

    public async Task<string?> AdvanceAsync(
        int id,
        OrderStatus expected,
        int? expectedSessionId = null)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

        var order = await db.Orders
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Id == id);

        if (order is null)
        {
            return "This order no longer exists.";
        }

        if (order.Status != expected)
        {
            return "Another user already updated this order. "
                + "Review its current status.";
        }

        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            return "A refunded order cannot be sent to the kitchen "
                + "or advanced.";
        }

        if (expectedSessionId.HasValue &&
            order.TableServiceSessionId != expectedSessionId.Value)
        {
            return "This order is no longer linked to the selected visit. " +
                   "Refresh the table.";
        }

        if (order.TableServiceSessionId.HasValue)
        {
            var reservationCancelled = await db.TableServiceSessions
                .AnyAsync(s =>
                    s.Id == order.TableServiceSessionId.Value &&
                    s.Reservation != null &&
                    s.Reservation.Status == ReservationStatus.Cancelled);

            if (reservationCancelled)
            {
                return "This order belongs to a cancelled reservation. " +
                       "Review it before continuing.";
            }
        }

        var next = Next(order);

        if (next is null)
        {
            return "This order cannot advance from its current status.";
        }

        if (expected == OrderStatus.Pending &&
            (order.Items.Count == 0 ||
             order.Items.Any(i =>
                 i.Quantity < 1 ||
                 i.Quantity > 99 ||
                 i.UnitPrice < 0)))
        {
            return "Check the order items before confirming. "
                + "An order must contain valid quantities and prices.";
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

        order.PaymentStatus = PaymentStatus.Paid;
        order.PaymentMethod = method;
        order.PaidAt = ReservationRules.MalaysiaNow;

        if (order.Status == OrderStatus.Served)
        {
            order.Status = OrderStatus.Completed;
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return null;
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
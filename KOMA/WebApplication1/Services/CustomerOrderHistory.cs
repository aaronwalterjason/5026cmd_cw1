using KOMA.Data;
using KOMA.Models;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Services;

public sealed record DiningHistory(string Reference, DateTime DiningAt, DateTime PaidAt,
    string Table, string Area, int? Guests, List<Order> Orders)
{
    public decimal Total => Orders.Sum(o => o.TotalAmount);
    public int DishCount => Orders.Sum(o => o.Items.Sum(i => i.Quantity));
}

public static class CustomerOrderHistory
{
    public static IQueryable<Order> OwnedOrders(ApplicationDbContext db, string customer) => db.Orders.AsNoTracking()
        .Where(o => o.CustomerId == customer || (o.CustomerId == null &&
            (o.Reservation!.CustomerUserId == customer || o.TableServiceSession!.Reservation!.CustomerUserId == customer ||
             (o.TableServiceSessionId != null && db.Orders.Any(owned =>
                 owned.TableServiceSessionId == o.TableServiceSessionId && owned.CustomerId == customer)))));

    public static IQueryable<Order> CandidateOrders(ApplicationDbContext db, string customer, DateTime now)
    {
        var owned = OwnedOrders(db, customer);
        var cutoff = now.AddMonths(-3);
        var recent = owned.Where(o => o.PaymentStatus == PaymentStatus.Paid && o.PaidAt >= cutoff && o.PaidAt <= now);
        // Load every order in a candidate visit, including unpaid add-ons, so partial payment
        // cannot incorrectly make a visit appear settled. Never use a table number as ownership.
        return owned.Where(o => recent.Any(r => r.Id == o.Id ||
                (o.TableServiceSessionId != null && r.TableServiceSessionId == o.TableServiceSessionId) ||
                ((o.ReservationId ?? o.TableServiceSession!.ReservationId) != null &&
                 (r.ReservationId ?? r.TableServiceSession!.ReservationId) == (o.ReservationId ?? o.TableServiceSession!.ReservationId))));
    }

    public static async Task<List<DiningHistory>> LoadAsync(ApplicationDbContext db, string customer, DateTime now)
    {
        var orders = await CandidateOrders(db, customer, now)
            .Include(o => o.Items).ThenInclude(i => i.MenuItem)
            .Include(o => o.RestaurantTable).ThenInclude(t => t!.DiningArea)
            .Include(o => o.Reservation)
            .Include(o => o.TableServiceSession).ThenInclude(s => s!.Reservation)
            .OrderBy(o => o.Id).ToListAsync();
        return Build(orders, now);
    }

    public static List<DiningHistory> Build(IEnumerable<Order> orders, DateTime now) => orders
        .Where(o => o.Status != OrderStatus.Cancelled && o.PaymentStatus != PaymentStatus.Refunded)
        .GroupBy(Key)
        .Where(g => g.All(o => o.PaymentStatus == PaymentStatus.Paid && o.PaidAt.HasValue && o.PaidAt <= now &&
            o.Status is not (OrderStatus.Pending or OrderStatus.AwaitingArrival)))
        .Where(g => g.Max(o => o.PaidAt!.Value) >= now.AddMonths(-3))
        .Select(g =>
        {
            var list = g.OrderBy(o => o.OrderedAt).ThenBy(o => o.Id).ToList();
            var first = list[0];
            var visit = list.Select(o => o.TableServiceSession).FirstOrDefault(s => s != null);
            var booking = list.Select(o => o.Reservation ?? o.TableServiceSession?.Reservation).FirstOrDefault(r => r != null);
            return new DiningHistory(g.Key, visit?.StartTime ?? booking?.StartTime ?? first.OrderedAt,
                list.Max(o => o.PaidAt!.Value), first.RestaurantTable?.TableNumber ?? "Not recorded",
                first.RestaurantTable?.DiningArea?.Name ?? "", visit?.GuestCount ?? booking?.GuestCount, list);
        })
        .OrderByDescending(v => v.PaidAt).ThenBy(v => v.Reference).ToList();

    private static string Key(Order order) => (order.ReservationId ?? order.TableServiceSession?.ReservationId) is int booking
        ? $"R-{booking:D5}" : order.TableServiceSessionId is int visit ? $"V-{visit:D5}" : $"O-{order.Id:D6}";
}

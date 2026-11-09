using KOMA.Models;
using KOMA.Services;
using KOMA.Data;
using Microsoft.EntityFrameworkCore;

internal static class OrderHistoryChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = new DateTime(2026, 11, 9, 12, 0, 0);
        var visit = new TableServiceSession { Id = 8, ReservationId = 5, GuestCount = 2, StartTime = now.AddHours(-2) };
        Order Paid(int id, decimal total, DateTime paidAt) => new()
        {
            Id = id, TableServiceSessionId = 8, TableServiceSession = visit,
            PaymentStatus = PaymentStatus.Paid, PaidAt = paidAt, TotalAmount = total,
            Status = OrderStatus.Served, OrderedAt = now.AddHours(-1),
            Items = [new OrderItem { ItemName = "Saved dish name", Quantity = 2, UnitPrice = total / 2 }]
        };
        var preorder = Paid(1, 40, now.AddMinutes(-5)); preorder.ReservationId = 5;
        var addon = Paid(2, 20, now.AddMinutes(-1));
        var result = CustomerOrderHistory.Build([preorder, addon], now);
        check(result.Count == 1 && result[0].Total == 60 && result[0].DishCount == 4, "History combines preorder and add-ons into one paid visit");
        check(result[0].Reference == "R-00005" && result[0].DiningAt == visit.StartTime, "History uses booking reference and actual dining time");
        addon.PaymentStatus = PaymentStatus.Unpaid; addon.PaidAt = null;
        check(CustomerOrderHistory.Build([preorder, addon], now).Count == 0, "Partially paid visit stays out of paid history");
        addon.Status = OrderStatus.Cancelled;
        check(CustomerOrderHistory.Build([preorder, addon], now).Single().Total == 40, "Cancelled add-ons do not inflate paid bill");
        preorder.PaidAt = now.AddMonths(-3);
        check(CustomerOrderHistory.Build([preorder], now).Count == 1, "Receipt is available exactly at three-month boundary");
        preorder.PaidAt = now.AddMonths(-3).AddSeconds(-1);
        check(CustomerOrderHistory.Build([preorder], now).Count == 0, "Older receipt cannot be returned by history details");
        preorder.PaidAt = now.AddMinutes(1);
        check(CustomerOrderHistory.Build([preorder], now).Count == 0, "Time simulator excludes receipts paid in its future");
        preorder.PaidAt = now; preorder.PaymentStatus = PaymentStatus.Refunded;
        check(CustomerOrderHistory.Build([preorder], now).Count == 0, "Refunded orders are excluded from paid receipts");
        var standalone = Paid(3, 15, now); standalone.TableServiceSessionId = null; standalone.TableServiceSession = null;
        check(CustomerOrderHistory.Build([standalone], now).Single().Reference == "O-000003", "Legacy paid order without session still has a receipt");
        standalone.Status = OrderStatus.AwaitingArrival;
        check(CustomerOrderHistory.Build([standalone], now).Count == 0, "Prepaid unarrived booking does not become a dining receipt");
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=QueryOnly;Trusted_Connection=True").Options);
        var sql = CustomerOrderHistory.CandidateOrders(db, "test-customer", now).ToQueryString();
        check(sql.Contains("test-customer") && sql.Contains("PaidAt") && sql.Contains("EXISTS"), "Scoped paid-visit history query translates to SQL Server");
    }
}

using Microsoft.EntityFrameworkCore;
using KOMA.Models;

namespace KOMA.Data;

// Creates sample orders so the Order List and Revenue Dashboard have data to show
// before the customer ordering pages exist. Runs only in Development, and only if
// some menu items have been added. To start again with fresh demo data: delete all
// rows from Orders and restart the app.
public static class DemoOrderSeeder
{
    // Demo customer accounts (password Customer#12345), created by SeedData in Development.
    public static readonly (string Email, string Name)[] DemoCustomers =
    {
        ("mei.tan@koma.local", "Tan Mei Ling"),
        ("faizal@koma.local", "Ahmad Faizal"),
        ("priya@koma.local", "Priya Ramasamy"),
        ("weijie@koma.local", "Lim Wei Jie"),
        ("aisyah@koma.local", "Nur Aisyah"),
        ("jason.wong@koma.local", "Jason Wong"),
    };

    // Names staff would type in for walk-in guests without an account.
    private static readonly string[] WalkInNames =
    {
        "Siti Rahmah", "David Lee", "Kumar Selvam", "Chong Kar Wai", "Farah Hani",
        "Ravi Shankar", "Emily Ng", "Hafiz Rahman", "Grace Ooi", "Arjun Nair"
    };

    public static async Task SeedAsync(ApplicationDbContext db)
    {
        // Demo orders made before CustomerName/OrderType existed have a blank name - replace them once.
        if (await db.Orders.AnyAsync(o => o.CustomerName == ""))
        {
            db.OrderItems.RemoveRange(db.OrderItems);
            db.Orders.RemoveRange(db.Orders);
            await db.SaveChangesAsync();
        }
        if (await db.Orders.AnyAsync()) return;

        var menu = await db.MenuItems.Where(m => m.Price > 0).ToListAsync();
        if (menu.Count == 0) return;   // add menu items in the admin pages first, then restart

        var tables = await db.RestaurantTables.Where(t => t.IsActive).Select(t => t.Id).ToListAsync();
        var emails = DemoCustomers.Select(c => c.Email).Append("customer@koma.local").ToList();
        var customers = await db.Users
            .Where(u => emails.Contains(u.Email!))
            .Select(u => new { u.Id, Name = u.FullName ?? u.Email! })
            .ToListAsync();

        var rng = new Random(5026);   // fixed seed = the same demo data every time
        var methods = Enum.GetValues<PaymentMethod>();
        var activeStatuses = new[] { OrderStatus.Pending, OrderStatus.Preparing, OrderStatus.ReadyToServe, OrderStatus.Served };
        var orders = new List<Order>();

        // ~120 past orders spread over the last 120 days, plus a few still in progress today.
        for (int i = 0; i < 124; i++)
        {
            bool today = i >= 120;
            var day = today ? DateTime.Today : DateTime.Today.AddDays(-rng.Next(1, 121));
            var orderedAt = day.AddHours(11).AddMinutes(rng.Next(0, 10 * 60 + 30));   // 11:00 - 21:30
            if (today && orderedAt > DateTime.Now) orderedAt = DateTime.Now.AddMinutes(-rng.Next(5, 60));

            // Roughly 40% online pre-orders, 30% table-tablet orders, 30% walk-ins.
            var roll = rng.Next(10);
            var type = customers.Count == 0 || roll >= 7 ? OrderType.WalkIn
                     : roll < 4 ? OrderType.OnlinePreOrder
                     : OrderType.TableTablet;

            var order = new Order
            {
                OrderType = type,
                RestaurantTableId = tables.Count > 0 ? tables[rng.Next(tables.Count)] : null,
                OrderedAt = type == OrderType.OnlinePreOrder ? orderedAt.AddHours(-rng.Next(1, 7)) : orderedAt
            };
            if (type == OrderType.WalkIn)
            {
                order.CustomerName = WalkInNames[rng.Next(WalkInNames.Length)];
            }
            else
            {
                var c = customers[rng.Next(customers.Count)];
                order.CustomerId = c.Id;
                order.CustomerName = c.Name;
            }

            foreach (var dish in menu.OrderBy(_ => rng.Next()).Take(rng.Next(1, Math.Min(4, menu.Count) + 1)))
                order.Items.Add(new OrderItem { MenuItemId = dish.Id, Quantity = rng.Next(1, 4), UnitPrice = dish.Price });
            order.TotalAmount = order.Items.Sum(x => x.UnitPrice * x.Quantity);

            if (today)
            {
                order.Status = activeStatuses[rng.Next(activeStatuses.Length)];
            }
            else if (rng.Next(10) == 0)
            {
                order.Status = OrderStatus.Cancelled;   // ~10% cancelled, never paid
            }
            else
            {
                order.Status = OrderStatus.Completed;
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaymentMethod = methods[rng.Next(methods.Length)];
                order.PaidAt = orderedAt.AddMinutes(rng.Next(40, 100));
            }
            orders.Add(order);
        }

        db.Orders.AddRange(orders);
        await db.SaveChangesAsync();
    }

    // Makes "today" look like a real service for the Today dashboard: a few paid orders from earlier
    // and live orders seated at the restaurant's actual tables. Runs on each start in Development,
    // but only adds orders when there are tables and nobody is currently seated at any of them.
    public static async Task EnsureTodayAsync(ApplicationDbContext db)
    {
        var active = new[] { OrderStatus.Pending, OrderStatus.Preparing, OrderStatus.ReadyToServe, OrderStatus.Served };
        var today = DateTime.Today;
        var now = DateTime.Now;

        // Demo orders left "in progress" from earlier days: finish and pay them, so they don't
        // show as waiting forever on the dashboard.
        var stale = await db.Orders.Where(o => o.OrderedAt < today && active.Contains(o.Status)).ToListAsync();
        foreach (var o in stale)
        {
            o.Status = OrderStatus.Completed;
            o.PaymentStatus = PaymentStatus.Paid;
            o.PaymentMethod ??= PaymentMethod.Cash;
            o.PaidAt ??= o.OrderedAt.AddHours(1);
        }

        var tables = await db.RestaurantTables.Where(t => t.IsActive && t.DiningArea!.IsActive).ToListAsync();
        if (tables.Count == 0) { await db.SaveChangesAsync(); return; }

        // Today's open orders with no table (their table was deleted): seat them at free tables.
        var openToday = await db.Orders.Where(o => o.OrderedAt >= today && active.Contains(o.Status)).ToListAsync();
        var taken = openToday.Where(o => o.RestaurantTableId != null).Select(o => o.RestaurantTableId!.Value).ToHashSet();
        foreach (var o in openToday.Where(o => o.RestaurantTableId == null))
        {
            var free = tables.FirstOrDefault(t => !taken.Contains(t.Id));
            if (free is null) break;
            o.RestaurantTableId = free.Id;
            taken.Add(free.Id);
        }
        await db.SaveChangesAsync();
        var needLive = taken.Count == 0;   // nobody seated yet -> seat some tables
        var needPaid = !await db.Orders.AnyAsync(o => o.PaymentStatus == PaymentStatus.Paid && o.PaidAt >= today);
        if (!needLive && !needPaid) return;

        var menu = await db.MenuItems.Where(m => m.Price > 0 && m.IsAvailable).ToListAsync();
        if (menu.Count == 0) return;
        var customers = await db.Users.Where(u => u.Email!.EndsWith("@koma.local") && u.Customer != null)
            .Select(u => new { u.Id, Name = u.FullName ?? u.Email! }).ToListAsync();

        var rng = new Random(now.DayOfYear);
        var methods = Enum.GetValues<PaymentMethod>();
        var orders = new List<Order>();

        Order Make(RestaurantTable table, DateTime at)
        {
            var roll = rng.Next(10);
            var type = customers.Count == 0 || roll >= 7 ? OrderType.WalkIn : roll < 4 ? OrderType.OnlinePreOrder : OrderType.TableTablet;
            var o = new Order { OrderType = type, RestaurantTableId = table.Id, OrderedAt = at };
            if (type == OrderType.WalkIn) o.CustomerName = WalkInNames[rng.Next(WalkInNames.Length)];
            else { var c = customers[rng.Next(customers.Count)]; o.CustomerId = c.Id; o.CustomerName = c.Name; }
            foreach (var dish in menu.OrderBy(_ => rng.Next()).Take(rng.Next(1, Math.Min(4, menu.Count) + 1)))
                o.Items.Add(new OrderItem { MenuItemId = dish.Id, Quantity = rng.Next(1, Math.Max(2, table.Capacity / 2) + 1), UnitPrice = dish.Price });
            o.TotalAmount = o.Items.Sum(x => x.UnitPrice * x.Quantity);
            return o;
        }

        // Earlier today: finished and paid (only for hours that have already passed).
        var opening = today.AddHours(11);
        for (int i = 0; needPaid && i < 10 && now > opening.AddHours(1); i++)
        {
            var at = opening.AddMinutes(rng.Next(0, (int)Math.Max(1, (now - opening).TotalMinutes - 90)));
            var o = Make(tables[rng.Next(tables.Count)], at);
            o.Status = OrderStatus.Completed;
            o.PaymentStatus = PaymentStatus.Paid;
            o.PaymentMethod = methods[rng.Next(methods.Length)];
            o.PaidAt = at.AddMinutes(rng.Next(40, 80));
            if (o.PaidAt > now) o.PaidAt = now.AddMinutes(-1);
            orders.Add(o);
        }

        // Right now: about 40% of tables are seated, at different stages of the meal.
        foreach (var table in tables.OrderBy(_ => rng.Next()).Take(needLive ? Math.Max(1, (int)Math.Round(tables.Count * 0.4)) : 0))
        {
            var o = Make(table, now.AddMinutes(-rng.Next(3, 75)));
            var minutes = (now - o.OrderedAt).TotalMinutes;
            o.Status = minutes < 10 ? OrderStatus.Pending
                     : minutes < 25 ? (rng.Next(2) == 0 ? OrderStatus.Preparing : OrderStatus.Pending)
                     : minutes < 45 ? (rng.Next(2) == 0 ? OrderStatus.ReadyToServe : OrderStatus.Preparing)
                     : OrderStatus.Served;
            orders.Add(o);
        }

        db.Orders.AddRange(orders);
        await db.SaveChangesAsync();
    }
}

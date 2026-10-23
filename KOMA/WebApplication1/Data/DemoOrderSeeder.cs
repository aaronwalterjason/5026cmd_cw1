using Microsoft.EntityFrameworkCore;
using KOMA.Models;

namespace KOMA.Data;

// Creates sample orders so the Order List and Revenue Dashboard have data to show
// Runs in Development. Adds missing Japanese menu categories/items before seeding orders.
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
        await SeedMenuAsync(db);
        // Demo orders made before CustomerName/OrderType existed have a blank name - replace them once.
        if (await db.Orders.AnyAsync(o => o.CustomerName == ""))
        {
            db.OrderItems.RemoveRange(db.OrderItems);
            db.Orders.RemoveRange(db.Orders);
            await db.SaveChangesAsync();
        }
        if (await db.Orders.AnyAsync()) return;

        var menu = await db.MenuItems.Where(m => m.Price > 0).ToListAsync();
        if (menu.Count == 0) return;

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
                order.Items.Add(new OrderItem { MenuItemId = dish.Id, ItemName = dish.Name, Quantity = rng.Next(1, 4), UnitPrice = dish.Price });
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

    // Sample coursework prices in RM, not a live restaurant price list.
    // Match names case-insensitively and preserve all existing admin changes, including availability.
    public static async Task SeedMenuAsync(ApplicationDbContext db)
    {
        var categories = await db.MenuCategories.ToListAsync();
        var existingItems = await db.MenuItems.ToListAsync();
        var existingNames = existingItems.Select(m => m.Name)
            .Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var menu = new (string Category, string Description, (string Name, decimal Price, string Description)[] Dishes)[]
        {
            ("Starters", "Small plates to begin your meal.", [
                ("Edamame", 8.90m, "Steamed young soybeans with sea salt."),
                ("Chuka Wakame", 9.90m, "Chilled seaweed salad with sesame dressing."),
                ("Agedashi Tofu", 12.90m, "Lightly fried tofu in warm dashi broth with grated daikon."),
                ("Chicken Gyoza", 14.90m, "Six pan-fried chicken and cabbage dumplings with dipping sauce.")
            ]),
            ("Salads & Soup", "Fresh salads and comforting bowls.", [
                ("Miso Soup", 5.90m, "Miso broth with tofu, wakame and spring onion."),
                ("Mushroom Clear Soup", 8.90m, "Light broth with shimeji mushrooms and spring onion."),
                ("Goma Green Salad", 12.90m, "Mixed greens, cucumber and tomato with sesame dressing."),
                ("Salmon Avocado Salad", 23.90m, "Salmon sashimi and avocado over crisp greens with citrus dressing.")
            ]),
            ("Sashimi", "Sliced seafood served with wasabi and pickled ginger.", [
                ("Salmon Sashimi", 26.90m, "Five slices of salmon sashimi."),
                ("Tuna Sashimi", 29.90m, "Five slices of tuna sashimi."),
                ("Octopus Sashimi", 24.90m, "Five slices of cooked octopus, served chilled."),
                ("Sashimi Trio", 49.90m, "Nine pieces featuring salmon, tuna and cooked octopus.")
            ]),
            ("Nigiri Sushi", "Hand-pressed sushi rice topped with seafood or egg.", [
                ("Salmon Nigiri", 10.90m, "Two pieces of salmon over seasoned sushi rice."),
                ("Tuna Nigiri", 12.90m, "Two pieces of tuna over seasoned sushi rice."),
                ("Ebi Nigiri", 9.90m, "Two pieces of cooked prawn over seasoned sushi rice."),
                ("Tamago Nigiri", 7.90m, "Two pieces of sweet Japanese omelette over sushi rice.")
            ]),
            ("Maki & Rolls", "Rolled favourites for sharing.", [
                ("Kappa Maki", 8.90m, "Six cucumber rolls wrapped in nori."),
                ("Salmon Maki", 12.90m, "Six salmon and sushi rice rolls wrapped in nori."),
                ("California Roll", 19.90m, "Eight pieces with crab stick, avocado, cucumber and fish roe."),
                ("Prawn Tempura Roll", 24.90m, "Eight pieces with crisp prawn tempura, cucumber and mayonnaise.")
            ]),
            ("Tempura & Fried Dishes", "Crisp, golden dishes served fresh.", [
                ("Ebi Tempura", 24.90m, "Four prawns in a light batter with tempura dipping sauce."),
                ("Vegetable Tempura", 16.90m, "Assorted seasonal vegetables in a light batter."),
                ("Chicken Karaage", 18.90m, "Japanese-style fried chicken with lemon and mayonnaise."),
                ("Takoyaki", 13.90m, "Six octopus balls with takoyaki sauce, mayonnaise and bonito flakes.")
            ]),
            ("Grilled Dishes", "Japanese grill favourites.", [
                ("Chicken Teriyaki", 22.90m, "Grilled chicken glazed with teriyaki sauce."),
                ("Salmon Teriyaki", 32.90m, "Grilled salmon fillet glazed with teriyaki sauce."),
                ("Saba Shioyaki", 23.90m, "Salt-grilled mackerel with grated daikon and lemon."),
                ("Chicken Yakitori", 16.90m, "Three chicken and spring onion skewers with tare glaze.")
            ]),
            ("Rice Bowls", "A satisfying meal over steamed Japanese rice.", [
                ("Chicken Katsudon", 24.90m, "Breaded chicken, egg and onion simmered over rice."),
                ("Gyudon", 27.90m, "Thinly sliced beef and onion in a savoury sauce over rice."),
                ("Unagi Don", 42.90m, "Grilled eel with sweet soy glaze over rice."),
                ("Chicken Katsu Curry", 28.90m, "Crisp chicken cutlet, Japanese curry and steamed rice.")
            ]),
            ("Noodles", "Warming bowls and refreshing cold noodles.", [
                ("Chicken Shoyu Ramen", 26.90m, "Ramen in soy-seasoned chicken broth with chicken, egg and nori."),
                ("Spicy Miso Ramen", 28.90m, "Ramen in spicy miso broth with chicken, corn and egg."),
                ("Ebi Tempura Udon", 27.90m, "Thick udon noodles in dashi broth with two tempura prawns."),
                ("Zaru Soba", 19.90m, "Chilled buckwheat noodles with dipping sauce and spring onion.")
            ]),
            ("Bento Sets", "Complete meals with rice, sides and miso soup.", [
                ("Chicken Teriyaki Bento", 32.90m, "Teriyaki chicken, rice, salad, tamago and miso soup."),
                ("Salmon Shioyaki Bento", 39.90m, "Salt-grilled salmon, rice, salad, tamago and miso soup."),
                ("Tempura Bento", 34.90m, "Prawn and vegetable tempura, rice, salad and miso soup."),
                ("Tofu Teriyaki Bento", 27.90m, "Teriyaki tofu, rice, vegetable sides, salad and miso soup.")
            ]),
            ("Desserts", "A sweet finish to your visit.", [
                ("Matcha Ice Cream", 9.90m, "Two scoops of green tea ice cream."),
                ("Black Sesame Ice Cream", 9.90m, "Two scoops of roasted black sesame ice cream."),
                ("Mochi Ice Cream Trio", 14.90m, "Three rice-cake-wrapped ice creams in assorted flavours."),
                ("Japanese Cheesecake", 15.90m, "A slice of light Japanese-style cheesecake.")
            ]),
            ("Drinks", "Tea, citrus refreshments and Japanese soft drinks.", [
                ("Hot Green Tea", 4.90m, "A cup of freshly brewed Japanese green tea."),
                ("Iced Matcha Latte", 12.90m, "Matcha green tea with milk over ice."),
                ("Yuzu Soda", 11.90m, "Sparkling citrus drink with yuzu flavour."),
                ("Ramune Original", 9.90m, "Japanese-style original ramune soda, served chilled.")
            ])
        };
        var recommended = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Salmon Sashimi", "Prawn Tempura Roll", "Chicken Karaage", "Chicken Katsu Curry", "Chicken Shoyu Ramen", "Chicken Teriyaki Bento", "Matcha Ice Cream", "Yuzu Soda" };
        foreach (var section in menu)
        {
            var category = categories.FirstOrDefault(c => string.Equals(c.Name.Trim(), section.Category, StringComparison.OrdinalIgnoreCase));
            if (category == null)
            {
                category = new MenuCategory { Name = section.Category, Description = section.Description, IsActive = true };
                categories.Add(category);
                db.MenuCategories.Add(category);
            }
            foreach (var dish in section.Dishes)
            {
                // The original California Roll keeps its existing uploaded image.
                var imageUrl = dish.Name == "California Roll" ? null
                    : $"/images/menu/{dish.Name.ToLowerInvariant().Replace(' ', '_')}.png";
                var previousImageUrl = dish.Name == "California Roll" ? null
                    : $"/images/menu/generated/{dish.Name.ToLowerInvariant().Replace(' ', '_')}.png";
                // Repair moved seed images without replacing photos uploaded by administrators.
                // Agedashi Tofu's known duplicate upload also points back to its named asset.
                foreach (var existing in existingItems.Where(m =>
                    string.Equals(m.Name.Trim(), dish.Name, StringComparison.OrdinalIgnoreCase)
                    && imageUrl != null
                    && (string.IsNullOrWhiteSpace(m.ImageUrl)
                        || string.Equals(m.ImageUrl, previousImageUrl, StringComparison.OrdinalIgnoreCase)
                        || (dish.Name == "Agedashi Tofu" && string.Equals(m.ImageUrl,
                            "/images/menu/067cdd2639004125a5a983313feda032.png", StringComparison.OrdinalIgnoreCase)))))
                    existing.ImageUrl = imageUrl;
                if (!existingNames.Add(dish.Name)) continue;
                db.MenuItems.Add(new MenuItem
                {
                    Name = dish.Name, Description = dish.Description, Price = dish.Price, ImageUrl = imageUrl,
                    MenuCategory = category, IsAvailable = true, IsRecommended = recommended.Contains(dish.Name),
                    IsSeasonal = dish.Name is "Vegetable Tempura" or "Zaru Soba" or "Mochi Ice Cream Trio"
                });
            }
        }
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
                o.Items.Add(new OrderItem { MenuItemId = dish.Id, ItemName = dish.Name, Quantity = rng.Next(1, Math.Max(2, table.Capacity / 2) + 1), UnitPrice = dish.Price });
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

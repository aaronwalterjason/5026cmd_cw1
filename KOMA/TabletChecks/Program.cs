using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.EntityFrameworkCore;

var count = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("PASS " + description); count++;
}
DemoClockChecks.Run(Check);
OrderHistoryChecks.Run(Check);
var settings = new RestaurantSettings { OpeningTime = new(10, 0), ClosingTime = new(22, 0) };
var day = new DateTime(2026, 11, 9);
Check(!DiningHours.At(settings, day.AddHours(21).AddMinutes(19)).Warn, "No warning before closing minus 40 minutes");
Check(DiningHours.At(settings, day.AddHours(21).AddMinutes(20)).Warn, "Warning starts exactly 10 minutes before last call");
Check(!DiningHours.At(settings, day.AddHours(21).AddMinutes(29).AddSeconds(59)).Locked, "Ordering allowed just before last call");
Check(DiningHours.At(settings, day.AddHours(21).AddMinutes(30)).Locked, "Ordering locked exactly at last call");
Check(DiningHours.At(settings, day.AddHours(9)).Locked, "Ordering locked before opening");
var overnight = new RestaurantSettings { OpeningTime = new(18, 0), ClosingTime = new(2, 0) };
Check(DiningHours.At(overnight, day.AddHours(1).AddMinutes(20)).Warn, "Overnight warning belongs to previous service day");
Check(DiningHours.At(overnight, day.AddHours(1).AddMinutes(30)).Locked, "Overnight last call enforced");
Check(!DiningHours.At(overnight, day.AddHours(23)).Locked, "Overnight ordering open before midnight");
var legacyOrder = new Order { Status = OrderStatus.ReadyToServe };
var legacyDish = new OrderItem { Status = DishStatus.Preparing };
Check(OrderWorkflowService.EffectiveDishStatus(legacyOrder, legacyDish) == DishStatus.Ready, "Existing ready orders display ready dishes");
legacyDish.Status = DishStatus.Served;
Check(OrderWorkflowService.EffectiveDishStatus(legacyOrder, legacyDish) == DishStatus.Served, "Partially served orders retain individual served status");
legacyOrder.Status = OrderStatus.Completed;
legacyDish.Status = DishStatus.Preparing;
Check(OrderWorkflowService.EffectiveDishStatus(legacyOrder, legacyDish) == DishStatus.Served, "Historical completed orders display served dishes");
legacyOrder.Status = OrderStatus.Preparing;
Check(OrderWorkflowService.EffectiveDishStatus(legacyOrder, legacyDish) == DishStatus.Preparing, "Preparing dishes are not shown as ready");

// A separate disposable LocalDB database; never connect to or modify the application's database.
var database = "KomaTabletChecks_" + Guid.NewGuid().ToString("N");
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer($"Server=(localdb)\\mssqllocaldb;Database={database};Trusted_Connection=True;TrustServerCertificate=True").Options;
await using var db = new ApplicationDbContext(options);
var queryService = new DiningOrderService(db);
var sql = queryService.NearbyReservations("customer", 1, day).ToQueryString();
Check(sql.Contains("DATEDIFF") && sql.Contains("CustomerUserId"), "Reservation window and nearest-booking query translates to SQL Server");
Check(queryService.ActiveVisits().ToQueryString().Contains("TableServiceSessions"), "Active visit query translates to SQL Server");
if (!args.Contains("--database"))
{
    Console.WriteLine($"Passed {count} offline checks. Use --database to run disposable LocalDB integration checks.");
    return;
}
var created = false;
try
{
    await db.Database.EnsureCreatedAsync();
    created = true;
    var now = ReservationRules.MalaysiaNow;
    settings.OpeningTime = TimeOnly.FromDateTime(now.AddHours(-2));
    settings.ClosingTime = TimeOnly.FromDateTime(now.AddHours(4));
    var account = new ApplicationUser { Id = "tablet-test", UserName = "tablet-test", FirstName = "Ching", FullName = "Ching Wei" };
    var area = new DiningArea { Name = "Test area" };
    var table = new RestaurantTable { TableNumber = "TEST-1", Capacity = 4, DiningArea = area };
    var other = new RestaurantTable { TableNumber = "TEST-2", Capacity = 4, DiningArea = area };
    var dish = new MenuItem { Name = "Rice", Price = 10, MenuCategory = new MenuCategory { Name = "Mains" } };
    db.AddRange(settings, account, table, other, dish);
    await db.SaveChangesAsync();
    var booking = new Reservation { RestaurantTableId = table.Id, CustomerUserId = account.Id, CustomerName = "Ching Wei",
        ContactNumber = "0123456789", CreatedByUserId = account.Id, StartTime = now.AddMinutes(10), EndTime = now.AddMinutes(100), HoldUntil = now.AddMinutes(40), GuestCount = 2 };
    db.Add(booking); await db.SaveChangesAsync();
    var preorder = new Order { ReservationId = booking.Id, RestaurantTableId = table.Id, CustomerId = account.Id,
        CustomerName = "Ching Wei", Status = OrderStatus.AwaitingArrival, TotalAmount = 20,
        Items = [new OrderItem { MenuItemId = dish.Id, ItemName = "Rice", Quantity = 2, UnitPrice = 10 }] };
    db.Add(preorder); await db.SaveChangesAsync();
    var dining = new DiningOrderService(db);
    var workflow = new OrderWorkflowService(db);
    Check((await dining.NearbyReservations(account.Id, table.Id, now).FirstAsync()).Id == booking.Id, "Account QR finds reservation ten minutes early");
    Check(!await dining.NearbyReservations("another-customer", table.Id, now).AnyAsync(), "QR cannot discover another customer's reservation");
    Check((await dining.BeginAsync(other.Id, account.Id, booking.Id)).Id == null, "Reservation cannot be claimed at the wrong table");
    var arrival = await dining.BeginAsync(table.Id, account.Id, booking.Id);
    Check(arrival.Id.HasValue, "Early arrival can confirm the reservation");
    var visit = arrival.Id!.Value;
    Check(await dining.ActiveVisits().AnyAsync(s => s.Id == visit), "Early arrival immediately becomes an active visit");
    var send = await dining.PlaceAsync(visit, table.Id, [], 20, 0, OrderWorkflowService.ReviewToken(preorder), account.Id);
    Check(send.Id == preorder.Id, "Confirmed preorder sent to kitchen");
    var addon = await dining.PlaceAsync(visit, table.Id, new() { [dish.Id] = 1 }, 10, preorder.Id, "", account.Id);
    Check(addon.Id.HasValue, "Add-on submitted as a separate order");
    Check(await db.Orders.Where(o => o.TableServiceSessionId == visit).SumAsync(o => o.TotalAmount) == 30, "Full visit bill includes preorder and add-on");
    Check((await dining.PlaceAsync(visit, table.Id, new() { [dish.Id] = 1 }, 10, preorder.Id, "", account.Id)).Id == null, "Stale submissions cannot duplicate orders");
    Check(await workflow.ServeDishAsync(preorder.Items.First().Id, visit, table.Id, "Tablet") != null, "Preparing dish cannot be marked served");
    Check(await workflow.AdvanceAsync(preorder.Id, OrderStatus.QueuedForKitchen) == null, "Kitchen starts preparation");
    Check(await workflow.AdvanceAsync(preorder.Id, OrderStatus.Preparing) == null, "Kitchen marks preorder ready");
    Check(preorder.Items.All(i => i.Status == DishStatus.Ready), "Kitchen ready action updates dish statuses");
    Check(await workflow.ServeDishAsync(preorder.Items.First().Id, visit, other.Id, "Tablet") != null, "Serving cannot cross table boundaries");
    Check(await workflow.ServeDishAsync(preorder.Items.First().Id, visit, table.Id, "Tablet") == null, "Ready dish can be marked served from tablet");
    Check(preorder.Status == OrderStatus.Served && preorder.Items.First().ServedVia == "Tablet", "Serving final dish advances order and records source");
    Check(await workflow.ServeDishAsync(preorder.Items.First().Id, visit, table.Id, "Staff") == null, "Repeated serving confirmation is idempotent");
    settings.ClosingTime = TimeOnly.FromDateTime(ReservationRules.MalaysiaNow.AddMinutes(20));
    await db.SaveChangesAsync();
    Check((await dining.PlaceAsync(visit, table.Id, new() { [dish.Id] = 1 }, 10, addon.Id!.Value, "", account.Id)).Id == null, "Server rejects new orders after last call");
    Console.WriteLine($"Passed {count} checks.");
}
finally
{
    if (created) await db.Database.EnsureDeletedAsync();
}

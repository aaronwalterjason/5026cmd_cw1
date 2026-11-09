using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin
{
    // "Today" dashboard (layout v7.3): sales so far compared by date, total orders by type, alerts,
    // who is seated where (live floor map) and today's booking schedule.
    // Longer-term figures live on the Revenue page.
    [Authorize(Roles = "Administrator")]
    public class DashboardModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public DashboardModel(ApplicationDbContext db) => _db = db;

        private static readonly OrderStatus[] ActiveStatuses =
        {
            OrderStatus.Pending,
            OrderStatus.QueuedForKitchen,
            OrderStatus.Preparing,
            OrderStatus.ReadyToServe,
            OrderStatus.Served
        };

        // ---- Today's numbers ----
        public DateTime Now { get; private set; }
        public decimal SalesToday { get; private set; }
        public decimal SalesSameDayLastWeek { get; private set; }
        public int OrdersToday { get; private set; }
        public int OrdersSameDayLastWeek { get; private set; }
        public int PaidOrdersToday { get; private set; }
        public int ItemsSoldToday { get; private set; }
        public decimal AverageOrder => PaidOrdersToday == 0 ? 0 : SalesToday / PaidOrdersToday;

        // ---- Charts / lists ----
        public List<HourBucket> OrdersByHour { get; private set; } = new();
        public IList<Order> LatestOrders { get; private set; } = new List<Order>();

        // ---- Live seating ----
        public IList<FloorLayout> Floors { get; private set; } = new List<FloorLayout>();
        public Dictionary<int, Order> SeatedOrderByTable { get; private set; } = new();   // tableId -> current order
        public int TablesTotal { get; private set; }
        public int TablesOccupied { get; private set; }
        public int TablesAwaitingPayment { get; private set; }
        public int SeatsTotal { get; private set; }
        public int SeatsInUse { get; private set; }

        // Details shown in the popup when a table on the live map is clicked, keyed by table id.
        // Sent with the page so the popup opens instantly (no extra page load).
        public string TablePopupJson { get; private set; } = "{}";
        public string OrderPopupJson { get; private set; } = "{}";   // same popup for Latest orders

        // ---- Dashboard v7.3: header, sales by date, order types, alerts, booking schedule ----
        public bool IsOpen { get; private set; }
        public string OpenText { get; private set; } = "";            // "Open until 10 PM" / "Closed · opens 10 AM"
        public Reservation? NextBooking { get; private set; }

        // Sales so far today vs the same time of day on earlier dates (yesterday, 1 and 2 weeks ago).
        public record SalesCompare(DateTime Day, decimal Amount, double? ChangePercent);
        public List<SalesCompare> SalesComparisons { get; private set; } = new();

        // Total orders, split by order type (first entry = all types).
        public record TypeStat(string Key, string Label, int Count, decimal Average, int LastWeek);
        public List<TypeStat> OrderTypeStats { get; private set; } = new();
        public string BusiestType { get; private set; } = "";
        public int BusiestShare { get; private set; }

        // Alerts: things an admin may need to step in on. Each disappears once it's fixed.
        public const int SlowOrderMinutes = 15;
        public IList<MenuItem> SoldOut { get; private set; } = new List<MenuItem>();
        public IList<Order> SlowOrders { get; private set; } = new List<Order>();
        public IList<Order> UnpaidTables { get; private set; } = new List<Order>();
        public int AlertCount => (SoldOut.Any() ? 1 : 0) + SlowOrders.Count + UnpaidTables.Count;

        // Booking schedule (agenda list) for today.
        public enum AgendaState { Arrived, Past, Next, Upcoming }
        public record AgendaRow(Reservation Booking, AgendaState State);
        public List<AgendaRow> Agenda { get; private set; } = new();

        // ---- Setup (small summary) ----
        public int MenuItemCount { get; private set; }
        public int ActivePromotionCount { get; private set; }

        public record HourBucket(int Hour, int Orders, decimal Sales);

        public async Task OnGetAsync()
        {
            Now = KOMA.Services.DemoClock.Shared.MalaysiaNow;
            var today = KOMA.Services.DemoClock.Shared.MalaysiaNow.Date;
            var tomorrow = today.AddDays(1);
            var lastWeek = today.AddDays(-7);

            // Today's orders (small, so load once and work in memory).
            var todays = await _db.Orders.AsNoTracking()
                .Include(o => o.Items).ThenInclude(i => i.MenuItem)
                .Include(o => o.RestaurantTable)
                .Where(o => o.OrderedAt >= today && o.OrderedAt < tomorrow)
                .ToListAsync();

            // One light query (no full records) for every "so far" figure: today, yesterday, 7 and 14 days ago.
            // This replaced 7 separate queries (paid today, last week sales/orders, 3 comparisons, last week by type).
            var since = today.AddDays(-14);
            var recent = await _db.Orders.AsNoTracking()
                .Where(o => o.OrderedAt >= since || (o.PaidAt != null && o.PaidAt >= since))
                .Select(o => new { o.OrderType, o.Status, o.PaymentStatus, o.OrderedAt, o.PaidAt, o.TotalAmount,
                    Qty = o.Items.Sum(i => (int?)i.Quantity) ?? 0 })
                .ToListAsync();
            var paidRecent = recent.Where(o => o.PaymentStatus == PaymentStatus.Paid && o.PaidAt != null).ToList();
            var paidToday = paidRecent.Where(o => o.PaidAt >= today && o.PaidAt < tomorrow).ToList();

            // Same day last week, up to the same time of day - a fair "so far" comparison.
            var lastWeekNow = lastWeek + (Now - today);
            SalesSameDayLastWeek = paidRecent.Where(o => o.PaidAt >= lastWeek && o.PaidAt < lastWeekNow).Sum(o => o.TotalAmount);
            OrdersSameDayLastWeek = recent.Count(o => o.OrderedAt >= lastWeek && o.OrderedAt < lastWeekNow && o.Status != OrderStatus.Cancelled);

            SalesToday = paidToday.Sum(o => o.TotalAmount);
            PaidOrdersToday = paidToday.Count;
            ItemsSoldToday = paidToday.Sum(o => o.Qty);
            var notCancelled = todays.Where(o => o.Status != OrderStatus.Cancelled).ToList();
            OrdersToday = notCancelled.Count;

            // Orders and paid sales per hour, from opening (or the first order) to closing/now.
            var first = Math.Min(10, notCancelled.Select(o => o.OrderedAt.Hour).DefaultIfEmpty(10).Min());
            var last = Math.Max(Math.Min(23, Math.Max(Now.Hour, 22)), notCancelled.Select(o => o.OrderedAt.Hour).DefaultIfEmpty(0).Max());
            OrdersByHour = Enumerable.Range(first, last - first + 1).Select(h => new HourBucket(
                h,
                notCancelled.Count(o => o.OrderedAt.Hour == h),
                paidToday.Where(o => o.PaidAt!.Value.Hour == h).Sum(o => o.TotalAmount))).ToList();

            // Open orders (unpaid, not cancelled) - used for live seating.
            var open = await _db.Orders.AsNoTracking()
                .Include(o => o.RestaurantTable)
                .Include(o => o.Items).ThenInclude(i => i.MenuItem)
                .Where(o => ActiveStatuses.Contains(o.Status) && o.PaymentStatus == PaymentStatus.Unpaid)
                .OrderBy(o => o.OrderedAt)
                .ToListAsync();

            // Live seating: a table is taken if it has an open (unpaid, not cancelled) order.
            Floors = await _db.FloorLayouts.AsNoTracking()
                .Include(f => f.DiningAreas).ThenInclude(a => a.Tables)
                .OrderBy(f => f.Level).ThenBy(f => f.Name)
                .ToListAsync();
            SeatedOrderByTable = open.Where(o => o.RestaurantTableId != null)
                .GroupBy(o => o.RestaurantTableId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.OrderedAt).First());

            var allTables = Floors.SelectMany(f => f.DiningAreas).Where(a => a.IsActive)
                .SelectMany(a => a.Tables).Where(t => t.IsActive).ToList();
            TablesTotal = allTables.Count;
            SeatsTotal = allTables.Sum(t => t.Capacity);
            TablesOccupied = allTables.Count(t => SeatedOrderByTable.ContainsKey(t.Id));
            TablesAwaitingPayment = allTables.Count(t => SeatedOrderByTable.TryGetValue(t.Id, out var o) && o.Status == OrderStatus.Served);
            SeatsInUse = allTables.Where(t => SeatedOrderByTable.ContainsKey(t.Id)).Sum(t => t.Capacity);

            LatestOrders = todays.OrderByDescending(o => o.OrderedAt).Take(6).ToList();

            BuildTablePopupJson();

            // ---------- Dashboard v7.3 ----------
            // Opening hours (single settings row; defaults if it's missing).
            var settings = await _db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync() ?? new RestaurantSettings();
            var nowTime = TimeOnly.FromDateTime(Now);
            IsOpen = nowTime >= settings.OpeningTime && nowTime < settings.ClosingTime;
            OpenText = IsOpen
                ? $"Open until {ShortTime(settings.ClosingTime)}"
                : nowTime < settings.OpeningTime ? $"Closed · opens {ShortTime(settings.OpeningTime)}" : "Closed for today";

            // Sales so far vs the same time of day on earlier dates.
            var sinceStart = Now - today;
            foreach (var daysBack in new[] { 1, 7, 14 })
            {
                var day = today.AddDays(-daysBack);
                var amount = paidRecent.Where(o => o.PaidAt >= day && o.PaidAt < day + sinceStart).Sum(o => o.TotalAmount);
                double? change = amount == 0 ? null : (double)((SalesToday - amount) / amount * 100);
                SalesComparisons.Add(new SalesCompare(day, amount, change));
            }

            // Orders by type (today so far vs the same weekday last week, same time of day).
            var lastWeekByType = recent
                .Where(o => o.OrderedAt >= lastWeek && o.OrderedAt < lastWeekNow && o.Status != OrderStatus.Cancelled)
                .GroupBy(o => o.OrderType).ToDictionary(g => g.Key, g => g.Count());
            OrderTypeStats.Add(new TypeStat("all", "All", OrdersToday, AverageOrder, OrdersSameDayLastWeek));
            foreach (var type in Enum.GetValues<OrderType>())
            {
                var paidOfType = paidToday.Where(o => o.OrderType == type).ToList();
                OrderTypeStats.Add(new TypeStat(type.ToString(), TypeLabel(type),
                    notCancelled.Count(o => o.OrderType == type),
                    paidOfType.Count == 0 ? 0 : paidOfType.Sum(o => o.TotalAmount) / paidOfType.Count,
                    lastWeekByType.GetValueOrDefault(type)));
            }
            var busiest = OrderTypeStats.Skip(1).OrderByDescending(s => s.Count).First();
            BusiestType = busiest.Count == 0 ? "" : busiest.Label;
            BusiestShare = OrdersToday == 0 ? 0 : (int)Math.Round(100.0 * busiest.Count / OrdersToday);

            // Alerts.
            var menu = await _db.MenuItems.AsNoTracking().ToListAsync();   // small table: one query gives both the count and sold-out list
            SoldOut = menu.Where(m => !m.IsAvailable).OrderBy(m => m.Name).ToList();
            var slowBefore = Now.AddMinutes(-SlowOrderMinutes);
            SlowOrders = open.Where(o => (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Preparing) && o.OrderedAt <= slowBefore)
                             .OrderBy(o => o.OrderedAt).ToList();
            UnpaidTables = SeatedOrderByTable.Values.Where(o => o.Status == OrderStatus.Served)
                             .OrderBy(o => o.OrderedAt).ToList();

            // Booking schedule: today's bookings in time order. "Arrived" = its pre-order / order has started.
            var bookings = await _db.Reservations.AsNoTracking()
                .Include(r => r.RestaurantTable)
                .Where(r => r.StartTime >= today && r.StartTime < tomorrow && r.Status != ReservationStatus.Cancelled)
                .OrderBy(r => r.StartTime).ThenBy(r => r.CustomerName)
                .ToListAsync();
            var bookingIds = bookings.Select(r => r.Id).ToList();
            var arrived = (await _db.Orders.AsNoTracking()
                .Where(o => o.ReservationId != null && bookingIds.Contains(o.ReservationId.Value)
                            && o.Status != OrderStatus.AwaitingArrival && o.Status != OrderStatus.Cancelled)
                .Select(o => o.ReservationId!.Value).ToListAsync()).ToHashSet();
            NextBooking = bookings.FirstOrDefault(r => r.StartTime >= Now && !arrived.Contains(r.Id));
            foreach (var r in bookings)
            {
                var state = arrived.Contains(r.Id) ? AgendaState.Arrived
                          : r.StartTime < Now ? AgendaState.Past
                          : r.Id == NextBooking?.Id ? AgendaState.Next
                          : AgendaState.Upcoming;
                Agenda.Add(new AgendaRow(r, state));
            }

            MenuItemCount = menu.Count;
            ActivePromotionCount = await _db.Promotions.CountAsync(p => p.IsActive);
        }

        private void BuildTablePopupJson()
        {
            var data = new Dictionary<string, object>();
            foreach (var f in Floors)
                foreach (var a in f.DiningAreas)
                    foreach (var t in a.Tables)
                    {
                        SeatedOrderByTable.TryGetValue(t.Id, out var o);
                        var state = StateOf(t, a);
                        data[t.Id.ToString()] = new
                        {
                            number = t.TableNumber,
                            floor = f.Name,
                            area = a.Name,
                            zone = a.Zone,
                            capacity = t.Capacity,
                            shape = t.Shape.ToString(),
                            state = state.ToString(),
                            stateLabel = state switch
                            {
                                SeatState.Occupied => "Seated",
                                SeatState.AwaitingPayment => "Awaiting payment",
                                SeatState.Inactive => "Closed",
                                _ => "Free"
                            },
                            order = o is null ? null : OrderInfo(o)
                        };
                    }
            TablePopupJson = System.Text.Json.JsonSerializer.Serialize(data, PopupJson);

            // Same popup for the Latest orders list, keyed by order id.
            var byOrder = new Dictionary<string, object>();
            foreach (var o in LatestOrders)
            {
                var (floor, area, table) = FindTable(o.RestaurantTableId);
                var active = ActiveStatuses.Contains(o.Status) && o.PaymentStatus == PaymentStatus.Unpaid;
                var state = o.Status == OrderStatus.Cancelled ? "Cancelled"
                          : active ? (o.Status == OrderStatus.Served ? "AwaitingPayment" : "Occupied")
                          : "Done";
                byOrder[o.Id.ToString()] = new
                {
                    number = table?.TableNumber ?? "—",
                    floor = floor?.Name ?? "",
                    area = area?.Name ?? "No table",
                    zone = area?.Zone ?? o.OrderType.DisplayName(),
                    capacity = table?.Capacity ?? 0,
                    shape = table?.Shape.ToString() ?? "",
                    state,
                    stateLabel = state switch
                    {
                        "Cancelled" => "Cancelled",
                        "AwaitingPayment" => "Awaiting payment",
                        "Occupied" => "Seated",
                        _ => o.Status.DisplayName()
                    },
                    order = OrderInfo(o)
                };
            }
            OrderPopupJson = System.Text.Json.JsonSerializer.Serialize(byOrder, PopupJson);
        }

        private static readonly System.Text.Json.JsonSerializerOptions PopupJson =
            new(System.Text.Json.JsonSerializerDefaults.Web);

        // The order part of a popup (shared by the live map and the Latest orders list).
        private object OrderInfo(Order o) => new
        {
            id = o.Id,
            number = o.OrderNumber,
            customer = o.CustomerName,
            type = o.OrderType.DisplayName(),
            status = o.Status.DisplayName(),
            step = Array.IndexOf(
                KOMA.Services.OrderWorkflowService.Steps,
                o.Status),
            cancelled = o.Status == OrderStatus.Cancelled,
            active = ActiveStatuses.Contains(o.Status) && o.PaymentStatus == PaymentStatus.Unpaid,
            orderedAt = o.OrderedAt.ToString("h:mm tt"),
            seatedFor = Minutes(o.OrderedAt, Now),
            payment = o.PaymentStatus.DisplayName(),
            total = o.TotalAmount.ToString("N2"),
            notes = o.Notes,
            items = o.Items.Select(i => new
            {
                name = i.DishName,
                qty = i.Quantity,
                line = i.LineTotal.ToString("N2")
            })
        };

        // Floor, area and table for a table id, from the floors already loaded for the live map.
        private (FloorLayout? Floor, DiningArea? Area, RestaurantTable? Table) FindTable(int? tableId)
        {
            if (tableId is null) return (null, null, null);
            foreach (var f in Floors)
                foreach (var a in f.DiningAreas)
                    foreach (var t in a.Tables)
                        if (t.Id == tableId) return (f, a, t);
            return (null, null, null);
        }

        // ---- Helpers for the page ----
        public enum SeatState { Free, Occupied, AwaitingPayment, Inactive }

        public SeatState StateOf(RestaurantTable t, DiningArea a)
        {
            if (!t.IsActive || !a.IsActive) return SeatState.Inactive;
            if (!SeatedOrderByTable.TryGetValue(t.Id, out var o)) return SeatState.Free;
            return o.Status == OrderStatus.Served ? SeatState.AwaitingPayment : SeatState.Occupied;
        }

        // e.g. "▲ 3 vs last Friday", "▼ RM 25.00 vs last Friday", "First sales this weekday".
        // Compared with the same weekday last week, up to the same time of day.
        public string CompareText(decimal now, decimal before, bool money, string firstText, out bool up)
        {
            up = now >= before;
            var day = Now.ToString("dddd");
            if (before == 0) return now == 0 ? "Nothing yet today" : firstText;
            if (now == before) return $"Same as last {day}";
            var diff = Math.Abs(now - before);
            var amount = money ? $"RM {diff:N2}" : diff.ToString("0");
            return $"{(now > before ? "▲" : "▼")} {amount} vs last {day}";
        }

        // Tables-in-use ring: share of tables seated (navy) and awaiting payment (rust).
        public double SeatedShare => TablesTotal == 0 ? 0 : (double)(TablesOccupied - TablesAwaitingPayment) / TablesTotal;
        public double PaymentShare => TablesTotal == 0 ? 0 : (double)TablesAwaitingPayment / TablesTotal;
        public int OccupiedPercent => TablesTotal == 0 ? 0 : (int)Math.Round(100.0 * TablesOccupied / TablesTotal);

        // Seated tables on one floor, and how many of them are waiting to pay (for the floor pills).
        public int SeatedOn(FloorLayout f) => f.DiningAreas.Where(a => a.IsActive).SelectMany(a => a.Tables)
            .Count(t => t.IsActive && SeatedOrderByTable.ContainsKey(t.Id));
        public int PayingOn(FloorLayout f) => f.DiningAreas.Where(a => a.IsActive).SelectMany(a => a.Tables)
            .Count(t => t.IsActive && SeatedOrderByTable.TryGetValue(t.Id, out var o) && o.Status == OrderStatus.Served);
        public string AreaUse(DiningArea a) =>
            $"{a.Tables.Count(t => t.IsActive && SeatedOrderByTable.ContainsKey(t.Id))}/{a.Tables.Count(t => t.IsActive)}";

        // Short labels used on the dashboard.
        public static string ShortTime(TimeOnly t) => t.Minute == 0 ? t.ToString("h tt") : t.ToString("h:mm tt");
        public static string ShortMinutes(DateTime since, DateTime now)
        {
            var m = Math.Max(0, (int)(now - since).TotalMinutes);
            return m < 60 ? $"{m}m" : $"{m / 60}h{m % 60:00}";
        }
        public static string TypeLabel(OrderType t) => t switch
        {
            OrderType.WalkIn => "Walk-in",
            OrderType.OnlinePreOrder => "Pre-order",
            OrderType.TableTablet => "Tablet",
            _ => t.ToString()
        };

        public static string Minutes(DateTime since, DateTime now)
        {
            var m = (int)(now - since).TotalMinutes;
            return m < 60 ? $"{m} min" : $"{m / 60} h {m % 60} min";
        }
    }
}

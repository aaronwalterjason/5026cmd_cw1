using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services;

namespace KOMA.Pages.Admin.Orders
{
    // Admin Orders page, "list + detail" layout:
    //   left  = filterable list of orders (in progress first, then earlier ones)
    //   right = the selected order in full, with buttons to move it along, take payment or cancel.
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly OrderWorkflowService _workflow;

        public IndexModel(
            ApplicationDbContext db,
            OrderWorkflowService workflow)
        {
            _db = db;
            _workflow = workflow;
        }

        private const int MaxRows = 200;

        // ---- Filters (query string, e.g. /Admin/Orders?show=unpaid&from=2026-10-01&id=42) ----
        [BindProperty(SupportsGet = true)] public string? Show { get; set; }      // all | active | unpaid | completed | cancelled
        [BindProperty(SupportsGet = true)] public OrderType? Type { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public int? Id { get; set; }           // selected order

        // ---- Results ----
        public List<Order> InProgress { get; private set; } = new();
        public List<Order> Earlier { get; private set; } = new();
        public Order? Selected { get; private set; }
        public List<TypeSummary> TypeSummaries { get; private set; } = new();
        public List<ShowChip> Chips { get; private set; } = new();
        public int MatchingOrders { get; private set; }
        public int ShownOrders => InProgress.Count + Earlier.Count;
        public bool IsTruncated => MatchingOrders > ShownOrders;
        public string ShowKey => Show is "active" or "unpaid" or "completed" or "cancelled" ? Show! : "all";

        public record TypeSummary(OrderType Type, int Count, decimal Sales);
        public record ShowChip(string Key, string Label, int Count);

        public static readonly OrderType[] TypeDisplayOrder =
            { OrderType.WalkIn, OrderType.OnlinePreOrder, OrderType.TableTablet };

        // Steps shown in the progress bar (Cancelled is shown separately).
        public static readonly OrderStatus[] Steps =
            OrderWorkflowService.Steps;

        // ---------- Quick lookup ----------
        // The page sends the orders once as compact data (last LookupDays days + anything still in progress),
        // and the browser does the searching, filtering and order switching with no further requests.
        // ?handler=List refreshes that data (polled every 20 s while the tab is open, "204" when nothing changed);
        // ?handler=Older searches orders older than the window on the server.
        public const int LookupDays = 90;
        private const int MaxLoad = 1500;

        public record ItemVm(int q, string n, decimal u);
        public record OrderVm(int id, string no, int type, string typeName, string tone, string? table, string? area,
            string name, string? email, string at, int status, string statusName, string statusTone, bool active,
            int pay, string payName, string? method, string? paidAt, decimal total, string? notes,
            List<ItemVm> items, int? next, string? nextLabel, bool nextPrimary, int step);
        public record LookupData(string version, string windowStart, List<OrderVm> orders);

        public string LookupJson { get; private set; } = "{}";
        public string MetaJson { get; private set; } = "{}";

        // =====================================================================
        // Today board (default view): one tile per seated table, each dish Preparing -> Ready -> Served,
        // customer help requests from the table tablet. ?view=all shows the order records (quick lookup below).
        // The board arrives as JSON (BoardJson) and the script draws it; it polls ?handler=Board every 5 s,
        // and every button posts to a handler that answers with the fresh board.
        // =====================================================================
        [BindProperty(SupportsGet = true)] public string? View { get; set; }
        public bool IsAll => string.Equals(View, "all", StringComparison.OrdinalIgnoreCase);
        public string BoardJson { get; private set; } = "{}";
        public int AllCount { get; private set; }
        public decimal AllPaid { get; private set; }

        // Orders that belong on the floor right now (pre-orders join once the guest has arrived).
        private static readonly OrderStatus[] BoardStatuses =
            { OrderStatus.Pending, OrderStatus.QueuedForKitchen, OrderStatus.Preparing, OrderStatus.ReadyToServe, OrderStatus.Served };

        public record BoardDish(int id, int orderId, int q, string n, int st, string? via);
        public record BoardOrder(int id, string no, string type, int status, string statusName, int pay, string payName, decimal total, string? notes);
        public record BoardHelp(int id, string? table, int? tableId, int type, string typeName, string? note, string at, string? takenBy);
        public record BoardTile(string key, string label, string? area, int? tableId, string name, string type, string tone, string since,
            List<BoardOrder> orders, List<BoardDish> dishes, decimal unpaid, BoardHelp? help);
        public record Board(string version, string now, List<BoardTile> tiles, List<BoardHelp> help);

        private async Task LoadSwitchAsync()
        {
            var all = await _db.Orders.AsNoTracking().GroupBy(o => 1)
                .Select(g => new { C = g.Count(), S = g.Sum(o => o.PaymentStatus == PaymentStatus.Paid ? o.TotalAmount : 0m) })
                .FirstOrDefaultAsync();
            AllCount = all?.C ?? 0;
            AllPaid = all?.S ?? 0;
        }

        private async Task<Board> BuildBoardAsync()
        {
            var rows = await _db.Orders.AsNoTracking()
                .Where(o => BoardStatuses.Contains(o.Status) && o.PaymentStatus != PaymentStatus.Refunded)
                .OrderBy(o => o.OrderedAt)
                .Select(o => new
                {
                    o.Id, o.Status, o.PaymentStatus, o.TotalAmount, o.OrderedAt, o.OrderType, o.Notes, o.CustomerName,
                    FullName = o.Customer != null ? o.Customer.FullName : null,
                    o.RestaurantTableId,
                    Table = o.RestaurantTable != null ? o.RestaurantTable.TableNumber : null,
                    Area = o.RestaurantTable != null && o.RestaurantTable.DiningArea != null ? o.RestaurantTable.DiningArea.Name : null,
                    Items = o.Items.OrderBy(i => i.Id).Select(i => new
                    {
                        i.Id, i.Quantity, i.Status, i.ServedVia,
                        Name = i.ItemName != null && i.ItemName != "" ? i.ItemName : i.MenuItem != null ? i.MenuItem.Name : "(removed item)"
                    }).ToList()
                })
                .ToListAsync();

            var help = (await _db.HelpRequests.AsNoTracking()
                    .Where(h => h.DoneAt == null)
                    .OrderBy(h => h.CreatedAt)
                    .Select(h => new { h.Id, h.RestaurantTableId, Table = h.RestaurantTable != null ? h.RestaurantTable.TableNumber : null, h.Type, h.Note, h.CreatedAt, h.TakenBy })
                    .ToListAsync())
                .Select(h => new BoardHelp(h.Id, h.Table, h.RestaurantTableId, (int)h.Type, h.Type.DisplayName(), h.Note, h.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"), h.TakenBy))
                .ToList();

            var tiles = rows
                .GroupBy(r => r.RestaurantTableId.HasValue ? "t" + r.RestaurantTableId : "o" + r.Id)
                .Select(g =>
                {
                    var first = g.First();
                    var dishes = g.SelectMany(o => o.Items.Select(i => new BoardDish(i.Id, o.Id, i.Quantity, i.Name,
                        // Orders already marked Served before dishes had their own status count as fully served.
                        o.Status == OrderStatus.Served ? (int)DishStatus.Served : (int)i.Status, i.ServedVia))).ToList();
                    var orders = g.Select(o => new BoardOrder(o.Id, $"KOMA-{o.Id:D6}", o.OrderType.DisplayName(), (int)o.Status, o.Status.DisplayName(),
                        (int)o.PaymentStatus, o.PaymentStatus.DisplayName(), o.TotalAmount, o.Notes)).ToList();
                    var name = !string.IsNullOrWhiteSpace(first.CustomerName) ? first.CustomerName : first.FullName ?? "Guest";
                    return new BoardTile(g.Key, first.Table ?? $"#{first.Id}", first.Area, first.RestaurantTableId, name,
                        first.OrderType.DisplayName(), TypeTone(first.OrderType), first.OrderedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                        orders, dishes, g.Where(o => o.PaymentStatus == PaymentStatus.Unpaid).Sum(o => o.TotalAmount),
                        help.FirstOrDefault(h => h.tableId != null && h.tableId == first.RestaurantTableId));
                })
                .OrderBy(t => t.label.Length).ThenBy(t => t.label)
                .ToList();

            var sig = string.Join("|", tiles.Select(t => t.key + ":" + string.Join(",", t.dishes.Select(d => d.id + "." + d.st)) + ":" + string.Join(",", t.orders.Select(o => o.status + "." + o.pay))))
                      + "#" + string.Join(",", help.Select(h => h.id + (h.takenBy ?? "")));
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(sig)))[..12];
            return new Board(hash, ReservationRules.MalaysiaNow.ToString("yyyy-MM-ddTHH:mm:ss"), tiles, help);
        }

        private async Task LoadBoardPageAsync()
        {
            var board = await BuildBoardAsync();
            BoardJson = System.Text.Json.JsonSerializer.Serialize(board, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default });
            MetaJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                payMethods = new[] { PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.TouchNGo, PaymentMethod.OnlineBanking }
                    .Select(m => new { v = m.ToString(), name = m.DisplayName() }),
                open = Id,
                dev = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment()
            });
        }

        // Fresh board for the script (204 = nothing changed since version v).
        public async Task<IActionResult> OnGetBoardAsync(string? v)
        {
            var board = await BuildBoardAsync();
            Response.Headers.CacheControl = "no-store";
            if (v == board.version) return StatusCode(204);
            return new JsonResult(board);
        }

        private string Me => User.Identity?.Name?.Split('@')[0] ?? "Staff";

        // Move one dish: to = 0 Preparing, 1 Ready, 2 Served (moving back is the undo).
        public async Task<IActionResult> OnPostDishAsync(int itemId, int to)
        {
            if (!Enum.IsDefined(typeof(DishStatus), to)) return BadRequest();
            var item = await _db.OrderItems.Include(i => i.Order!).ThenInclude(o => o.Items).FirstOrDefaultAsync(i => i.Id == itemId);
            if (item?.Order is null) return NotFound();
            SetDish(item, (DishStatus)to);
            SyncOrder(item.Order);
            await _db.SaveChangesAsync();
            return new JsonResult(await BuildBoardAsync());
        }

        // "Serve all ready" on a table: every listed dish that is Ready becomes Served.
        public async Task<IActionResult> OnPostServeReadyAsync(int[] ids)
        {
            var items = await _db.OrderItems.Include(i => i.Order!).ThenInclude(o => o.Items)
                .Where(i => ids.Contains(i.Id) && i.Status == DishStatus.Ready).ToListAsync();
            foreach (var i in items) SetDish(i, DishStatus.Served);
            foreach (var o in items.Select(i => i.Order!).Distinct()) SyncOrder(o);
            await _db.SaveChangesAsync();
            return new JsonResult(await BuildBoardAsync());
        }

        private static void SetDish(OrderItem item, DishStatus to)
        {
            // An order marked Served before dishes had their own status: its dishes were all served.
            if (item.Order!.Status == OrderStatus.Served)
                foreach (var other in item.Order.Items.Where(x => x.Status != DishStatus.Served)) { other.Status = DishStatus.Served; other.ServedVia ??= "Staff"; }
            var now = ReservationRules.MalaysiaNow;
            item.Status = to;
            item.ReadyAt = to >= DishStatus.Ready ? item.ReadyAt ?? now : null;
            item.ServedAt = to == DishStatus.Served ? now : null;
            item.ServedVia = to == DishStatus.Served ? "Staff" : null;
        }

        // Keep the order's own status in step with its dishes:
        //   first dish ready/served -> the kitchen has it (Preparing); every dish served -> Served
        //   (and Completed if it was already paid); a served dish moved back -> Preparing again.
        private static void SyncOrder(Order o)
        {
            if (o.Status is OrderStatus.Cancelled or OrderStatus.Completed or OrderStatus.AwaitingArrival) return;
            var all = o.Items.Count > 0 && o.Items.All(i => i.Status == DishStatus.Served);
            var any = o.Items.Any(i => i.Status != DishStatus.Preparing);
            if (all)
            {
                o.Status = o.PaymentStatus == PaymentStatus.Paid ? OrderStatus.Completed : OrderStatus.Served;
            }
            else if (o.Status is OrderStatus.Served or OrderStatus.ReadyToServe || (any && o.Status is OrderStatus.Pending or OrderStatus.QueuedForKitchen))
            {
                o.Status = OrderStatus.Preparing;
            }
        }

        // Help requests: act = take ("I'll take it") or done.
        public async Task<IActionResult> OnPostHelpAsync(int id, string act)
        {
            var h = await _db.HelpRequests.FindAsync(id);
            if (h is null) return NotFound();
            var now = ReservationRules.MalaysiaNow;
            if (act == "take") { h.TakenBy = Me; h.TakenAt = now; }
            else if (act == "done") { h.DoneBy = Me; h.DoneAt = now; }
            else if (act == "undo") { h.DoneBy = null; h.DoneAt = null; }
            await _db.SaveChangesAsync();
            return new JsonResult(await BuildBoardAsync());
        }

        // Development only: create a help request as if a table tablet sent it (the tablet isn't built yet).
        public async Task<IActionResult> OnPostTestHelpAsync(int? tableId, int type, string? note)
        {
            if (!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment()) return NotFound();
            _db.HelpRequests.Add(new HelpRequest
            {
                RestaurantTableId = tableId, Type = Enum.IsDefined(typeof(HelpType), type) ? (HelpType)type : HelpType.CallStaff,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 200)],
                CreatedAt = ReservationRules.MalaysiaNow
            });
            await _db.SaveChangesAsync();
            return new JsonResult(await BuildBoardAsync());
        }

        public async Task OnGetAsync()
        {
            await LoadSwitchAsync();
            if (!IsAll) { await LoadBoardPageAsync(); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var data = await LoadLookupAsync(From);
            Response.Headers["Server-Timing"] = $"db;desc=\"orders query\";dur={sw.Elapsed.TotalMilliseconds:0}";

            var json = new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default };
            LookupJson = System.Text.Json.JsonSerializer.Serialize(data, json);
            MetaJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                steps = Steps.Select(s => new { v = (int)s, name = s.DisplayName() }),
                types = TypeDisplayOrder.Select(t => new { v = (int)t, name = t.DisplayName(), tone = TypeTone(t) }),
                payMethods = new[] { PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.TouchNGo, PaymentMethod.OnlineBanking }
                    .Select(m => new { v = m.ToString(), name = m.DisplayName() }),
                state = new { show = ShowKey, type = Type.HasValue ? (int?)Type.Value : null, from = From?.ToString("yyyy-MM-dd"),
                    to = To?.ToString("yyyy-MM-dd"), search = Search ?? "", id = Id }
            }, json);
        }

        // Fresh copy of the data. v = the version the page already has: returns 204 (nothing) when unchanged.
        public async Task<IActionResult> OnGetListAsync(string? v)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var data = await LoadLookupAsync(From);
            Response.Headers["Server-Timing"] = $"db;dur={sw.Elapsed.TotalMilliseconds:0}";
            Response.Headers.CacheControl = "no-store";
            if (v == data.version) return StatusCode(204);   // nothing changed (PageModel has no NoContent())
            return new JsonResult(data);
        }

        // Search orders older than the loaded window (by number, customer name/email or table). Newest 50.
        public async Task<IActionResult> OnGetOlderAsync(string? q, string? before)
        {
            if (string.IsNullOrWhiteSpace(q) || !DateTime.TryParse(before, out var until)) return new JsonResult(Array.Empty<OrderVm>());
            var term = q.Trim();
            int.TryParse(term.ToUpperInvariant().Replace("KOMA-", ""), out var id);
            var rows = await Project(_db.Orders.AsNoTracking()
                    .Where(o => o.OrderedAt < until)
                    .Where(o => o.Id == id || o.CustomerName.Contains(term)
                        || (o.Customer != null && o.Customer.Email!.Contains(term))
                        || (o.RestaurantTable != null && o.RestaurantTable.TableNumber.Contains(term))
                        || o.Items.Any(i => i.ItemName.Contains(term)))
                    .OrderByDescending(o => o.OrderedAt).Take(50))
                .ToListAsync();
            return new JsonResult(rows.Select(ToVm).ToList());
        }

        private async Task<LookupData> LoadLookupAsync(DateTime? from)
        {
            var start = DateTime.Today.AddDays(-LookupDays);
            if (from.HasValue && from.Value.Date < start) start = from.Value.Date;   // the date filter asked for older orders
            var rows = await Project(_db.Orders.AsNoTracking()
                    .Where(o => o.OrderedAt >= start || (o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled))
                    .OrderByDescending(o => o.OrderedAt).Take(MaxLoad))
                .ToListAsync();
            var orders = rows.Select(ToVm).ToList();
            // Version = a short fingerprint of everything shown, so the page can tell when anything changed.
            var sig = string.Join("|", rows.Select(r => $"{r.Id}:{(int)r.Status}:{(int)r.PaymentStatus}:{r.TotalAmount}:{r.Items.Count}"));
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(sig)))[..12];
            return new LookupData(hash, start.ToString("yyyy-MM-dd"), orders);
        }

        // Only the columns the page needs (no full entities, no change tracking).
        private record Row(int Id, OrderType OrderType, string? Table, string? Area, string CustomerName, string? FullName, string? Email,
            DateTime OrderedAt, OrderStatus Status, PaymentStatus PaymentStatus, PaymentMethod? PaymentMethod, DateTime? PaidAt,
            decimal TotalAmount, string? Notes, List<ItemVm> Items);
        private static IQueryable<Row> Project(IQueryable<Order> q) => q.Select(o => new Row(
            o.Id, o.OrderType,
            o.RestaurantTable != null ? o.RestaurantTable.TableNumber : null,
            o.RestaurantTable != null && o.RestaurantTable.DiningArea != null ? o.RestaurantTable.DiningArea.Name : null,
            o.CustomerName, o.Customer != null ? o.Customer.FullName : null, o.Customer != null ? o.Customer.Email : null,
            o.OrderedAt, o.Status, o.PaymentStatus, o.PaymentMethod, o.PaidAt, o.TotalAmount, o.Notes,
            o.Items.OrderBy(i => i.Id).Select(i => new ItemVm(i.Quantity,
                i.ItemName != null && i.ItemName != "" ? i.ItemName : i.MenuItem != null ? i.MenuItem.Name : "(removed item)", i.UnitPrice)).ToList()));

        private static OrderVm ToVm(Row r)
        {
            var next = OrderWorkflowService.Next(new Order { Status = r.Status, PaymentStatus = r.PaymentStatus, OrderType = r.OrderType });
            return new OrderVm(r.Id, $"KOMA-{r.Id:D6}", (int)r.OrderType, r.OrderType.DisplayName(), TypeTone(r.OrderType),
                r.Table, r.Area,
                !string.IsNullOrWhiteSpace(r.CustomerName) ? r.CustomerName : r.FullName ?? "Guest", r.Email,
                r.OrderedAt.ToString("yyyy-MM-ddTHH:mm:ss"), (int)r.Status, r.Status.DisplayName(), StatusTone(r.Status), IsActive(r.Status),
                (int)r.PaymentStatus, r.PaymentStatus.DisplayName(), r.PaymentMethod?.DisplayName(),
                r.PaidAt?.ToString("yyyy-MM-ddTHH:mm:ss"), r.TotalAmount, r.Notes, r.Items,
                next.HasValue ? (int)next.Value : null, next.HasValue ? NextLabel(next.Value) : null,
                !(r.PaymentStatus == PaymentStatus.Unpaid && r.Status == OrderStatus.Served),
                Array.IndexOf(Steps, r.Status));
        }

        // Just the right-hand panel for one order - used by the page script when a row is clicked,
        // so switching orders runs one small query instead of reloading the whole page.
        public async Task<IActionResult> OnGetDetailAsync()
        {
            if (Id is null) return NotFound();
            Selected = await LoadOrderAsync(Id.Value);
            if (Selected is null) return NotFound();
            return Partial("_OrderDetail", this);
        }

        private Task<Order?> LoadOrderAsync(int id) =>
            _db.Orders.AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.RestaurantTable).ThenInclude(t => t!.DiningArea)
                .Include(o => o.Items).ThenInclude(i => i.MenuItem)
                .FirstOrDefaultAsync(o => o.Id == id);   // one round trip (only Items is a collection, so no split needed)

        // ---------- Actions on the selected order ----------

        // Move the order to its next step (Pending -> Preparing -> Ready to serve -> Served -> Completed).
        public Task<IActionResult> OnPostAdvanceAsync(
            int orderId,
            OrderStatus? expectedStatus) =>
            RunActionAsync(
                orderId,
                () => expectedStatus.HasValue && ModelState.IsValid
                    ? _workflow.AdvanceAsync(orderId, expectedStatus.Value)
                    : Task.FromResult<string?>(
                        "Reload this page before advancing the order."),
                "Order advanced successfully.");

        // Record payment. A served order that is paid is finished, so it becomes Completed.
        public Task<IActionResult> OnPostPayAsync(
            int orderId,
            PaymentMethod method) =>
            RunActionAsync(
                orderId,
                () => ModelState.IsValid
                    ? _workflow.PayAsync(orderId, method)
                    : Task.FromResult<string?>(
                        "Choose a valid payment method."),
                "Payment recorded.");

        // Cancel an order that is not finished. A paid order is marked Refunded so it leaves revenue.
        public Task<IActionResult> OnPostCancelAsync(
            int orderId,
            OrderStatus? expectedStatus) =>
            RunActionAsync(
                orderId,
                () => expectedStatus.HasValue && ModelState.IsValid
                    ? _workflow.CancelAsync(orderId, expectedStatus.Value)
                    : Task.FromResult<string?>(
                        "Reload this page before cancelling the order."),
                "Order cancelled.");

        private async Task<IActionResult> RunActionAsync(
            int orderId,
            Func<Task<string?>> action,
            string success)
        {
            try
            {
                TempData["StatusMessage"] = await action() ?? success;
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                TempData["StatusMessage"] =
                    "The database was busy or unavailable. "
                    + "Refresh and check the order before retrying.";
            }
            catch (DbUpdateException)
            {
                TempData["StatusMessage"] =
                    "The order could not be updated. "
                    + "Refresh and check its current status.";
            }

            return RedirectToPage(
                "./Index",
                new Microsoft.AspNetCore.Routing.RouteValueDictionary(
                    (object)Route(orderId)));
        }

        // ---------- Helpers used by the page ----------

        // Route values for links/forms that keep the current filters. Pass a value to change it.
        private const string Keep = "\u0001";
        public Dictionary<string, string> Route(int? id = null, string? show = Keep, string? type = Keep)
        {
            var r = new Dictionary<string, string>();
            void Add(string key, string? value) { if (!string.IsNullOrEmpty(value)) r[key] = value; }
            Add("view", View);
            Add("show", show == Keep ? Show : show);
            Add("type", type == Keep ? Type?.ToString() : type);
            Add("from", From?.ToString("yyyy-MM-dd"));
            Add("to", To?.ToString("yyyy-MM-dd"));
            Add("search", Search);
            if (id.HasValue) r["id"] = id.Value.ToString();
            return r;
        }

        public static bool IsActive(OrderStatus s) => s != OrderStatus.Completed && s != OrderStatus.Cancelled;

        public static OrderStatus? NextStatus(Order o) =>
            OrderWorkflowService.Next(o);

        public static string NextLabel(OrderStatus next) => next switch
        {
            OrderStatus.QueuedForKitchen => "Confirm & send to kitchen",
            OrderStatus.Preparing => "Start preparing",
            OrderStatus.ReadyToServe => "Mark ready to serve",
            OrderStatus.Served => "Mark served",
            OrderStatus.Completed => "Complete order",
            _ => next.DisplayName()
        };

        // "12 min" / "1 h 05 min" since the order was placed.
        public static string Elapsed(Order o)
        {
            var span = DateTime.Now - o.OrderedAt;
            if (span.TotalMinutes < 1) return "just now";
            return span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes:D2} min" : $"{(int)span.TotalMinutes} min";
        }

        // KOMA-theme status pill (styled in koma-theme.css: .ord-st-*).
        public static string StatusTone(OrderStatus status) => "ord-st-" + status.ToString().ToLowerInvariant();

        // Dot colour for each order type (navy / rust / sand, same as the Revenue page).
        public static string TypeTone(OrderType type) => type switch
        {
            OrderType.OnlinePreOrder => "ord-ty-online",
            OrderType.TableTablet => "ord-ty-tablet",
            _ => "ord-ty-walkin"
        };

        // ---- Kept for the Today dashboard, which reuses these ----

        public static string BadgeClass(OrderStatus status) => status switch
        {
            OrderStatus.Pending => "bg-secondary",
            OrderStatus.Preparing => "bg-warning text-dark",
            OrderStatus.ReadyToServe => "bg-info text-dark",
            OrderStatus.Served => "bg-primary",
            OrderStatus.Completed => "bg-success",
            OrderStatus.Cancelled => "bg-danger",
            OrderStatus.QueuedForKitchen => "bg-light text-dark",
            _ => "bg-light text-dark"
        };

        // Always show a person's name: the name saved on the order, else the account's name.
        public static string CustomerName(Order o) =>
            !string.IsNullOrWhiteSpace(o.CustomerName) ? o.CustomerName
            : o.Customer?.FullName ?? "Guest";

        public static string TypeBadgeClass(OrderType type) => type switch
        {
            OrderType.OnlinePreOrder => "bg-primary-subtle text-primary-emphasis border border-primary-subtle",
            OrderType.TableTablet => "bg-success-subtle text-success-emphasis border border-success-subtle",
            _ => "bg-warning-subtle text-warning-emphasis border border-warning-subtle"
        };

        public static string TypeIcon(OrderType type) => type switch
        {
            OrderType.OnlinePreOrder => "🌐",
            OrderType.TableTablet => "📱",
            _ => "🚶"
        };
    }
}

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

        public async Task OnGetAsync()
        {
            // Date + search filters apply to everything on the page.
            var query = _db.Orders.AsNoTracking().AsQueryable();
            if (From.HasValue) query = query.Where(o => o.OrderedAt >= From.Value.Date);
            if (To.HasValue) query = query.Where(o => o.OrderedAt < To.Value.Date.AddDays(1));
            if (!string.IsNullOrWhiteSpace(Search))
            {
                var term = Search.Trim();
                int.TryParse(term.ToUpperInvariant().Replace("KOMA-", ""), out var id);
                query = query.Where(o =>
                    o.Id == id ||
                    o.CustomerName.Contains(term) ||
                    (o.Customer != null && o.Customer.Email!.Contains(term)) ||
                    (o.RestaurantTable != null && o.RestaurantTable.TableNumber.Contains(term)));
            }

            // Order types strip (ignores the type filter so all three stay visible).
            var byType = await query.GroupBy(o => o.OrderType)
                .Select(g => new { g.Key, Count = g.Count(), Sales = g.Sum(o => o.PaymentStatus == PaymentStatus.Paid ? o.TotalAmount : 0m) })
                .ToListAsync();
            TypeSummaries = TypeDisplayOrder.Select(t =>
            {
                var row = byType.FirstOrDefault(x => x.Key == t);
                return new TypeSummary(t, row?.Count ?? 0, row?.Sales ?? 0);
            }).ToList();

            if (Type.HasValue) query = query.Where(o => o.OrderType == Type.Value);

            // Counts for the "show" chips.
            var counts = await query.GroupBy(o => new { o.Status, o.PaymentStatus })
                .Select(g => new { g.Key.Status, g.Key.PaymentStatus, Count = g.Count() })
                .ToListAsync();
            Chips = new()
            {
                new("all", "All", counts.Sum(c => c.Count)),
                new("active", "In progress", counts.Where(c => IsActive(c.Status)).Sum(c => c.Count)),
                new("unpaid", "Unpaid", counts.Where(c => c.PaymentStatus == PaymentStatus.Unpaid && c.Status != OrderStatus.Cancelled).Sum(c => c.Count)),
                new("completed", "Completed", counts.Where(c => c.Status == OrderStatus.Completed).Sum(c => c.Count)),
                new("cancelled", "Cancelled", counts.Where(c => c.Status == OrderStatus.Cancelled).Sum(c => c.Count)),
            };

            query = ShowKey switch
            {
                "active" => query.Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled),
                "unpaid" => query.Where(o => o.PaymentStatus == PaymentStatus.Unpaid && o.Status != OrderStatus.Cancelled),
                "completed" => query.Where(o => o.Status == OrderStatus.Completed),
                "cancelled" => query.Where(o => o.Status == OrderStatus.Cancelled),
                _ => query
            };

            MatchingOrders = await query.CountAsync();
            var list = await query
                .Include(o => o.Customer)
                .Include(o => o.RestaurantTable)
                .OrderByDescending(o => o.OrderedAt)
                .Take(MaxRows)
                .ToListAsync();
            InProgress = list.Where(o => IsActive(o.Status)).ToList();
            Earlier = list.Where(o => !IsActive(o.Status)).ToList();

            // Selected order: the one in the URL, otherwise the newest in-progress one, otherwise the newest.
            var selectedId = Id ?? InProgress.FirstOrDefault()?.Id ?? Earlier.FirstOrDefault()?.Id;
            if (selectedId.HasValue) Selected = await LoadOrderAsync(selectedId.Value);
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
                .AsSplitQuery()
                .FirstOrDefaultAsync(o => o.Id == id);

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

using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Staff.Orders;

public abstract class OrderBoardModel(
    ApplicationDbContext db,
    OrderWorkflowService workflow) : PageModel
{
    public abstract bool Kitchen { get; }

    public string Heading =>
        Kitchen ? "Kitchen queue" : "Customer orders";

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public List<Order> Orders { get; private set; } = [];

    public int Total { get; private set; }

    public int TotalPages =>
        Math.Max(1, (int)Math.Ceiling(Total / 20d));

    public OrderStatus SelectedStatus { get; private set; }

    public OrderStatus[] VisibleStatuses => Kitchen
        ?
        [
            OrderStatus.QueuedForKitchen,
            OrderStatus.Preparing,
            OrderStatus.ReadyToServe
        ]
        :
        [
            OrderStatus.Pending,
            OrderStatus.QueuedForKitchen,
            OrderStatus.Preparing,
            OrderStatus.ReadyToServe,
            OrderStatus.Served
        ];

    public async Task OnGetAsync()
    {
        SelectedStatus =
            Enum.TryParse<OrderStatus>(Filter, out var status) &&
            VisibleStatuses.Contains(status)
                ? status
                : VisibleStatuses[0];

        Filter = SelectedStatus.ToString();

        var query = db.Orders
            .AsNoTracking()
            .Where(o => o.Status == SelectedStatus);

        Total = await query.CountAsync();
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);

        Orders = await query
            .Include(o => o.RestaurantTable)
            .Include(o => o.Items)
                .ThenInclude(i => i.MenuItem)
            .OrderBy(o => o.OrderedAt)
            .ThenBy(o => o.Id)
            .Skip((PageNumber - 1) * 20)
            .Take(20)
            .ToListAsync();
    }

    public bool CanAdvance(OrderStatus status) => Kitchen
        ? status is OrderStatus.QueuedForKitchen
            or OrderStatus.Preparing
        : status is OrderStatus.Pending
            or OrderStatus.ReadyToServe;

    public async Task<IActionResult> OnPostAdvanceAsync(
        int orderId,
        OrderStatus? expectedStatus)
    {
        if (!ModelState.IsValid ||
            expectedStatus is null ||
            !CanAdvance(expectedStatus.Value))
        {
            return BadRequest();
        }

        try
        {
            var error = await workflow.AdvanceAsync(
                orderId,
                expectedStatus.Value);

            if (error != null)
            {
                TempData["OrderError"] = error;
            }
            else
            {
                TempData["OrderSuccess"] =
                    expectedStatus.Value switch
                    {
                        OrderStatus.Pending =>
                            "Order confirmed. It is now available "
                            + "in the kitchen queue.",

                        OrderStatus.QueuedForKitchen =>
                            "Preparation started.",

                        OrderStatus.Preparing =>
                            "Order marked ready for serving.",

                        _ => "Order marked served."
                    };
            }
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            TempData["OrderError"] =
                "The database was busy or unavailable. "
                + "Refresh and check the order before retrying.";
        }
        catch (DbUpdateException)
        {
            TempData["OrderError"] =
                "The order could not be updated. "
                + "Refresh and review its current status.";
        }

        return RedirectToPage(new { Filter, PageNumber });
    }
}
using System.Security.Claims;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Reservations;

[Authorize(Roles = "Customer,Staff,Administrator")]
public class PreOrderModel(ApplicationDbContext context, DiningOrderService dining, KOMA.Services.Email.BookingEmailService emails) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty] public Dictionary<int, int> Quantities { get; set; } = [];
    public List<MenuItem> Menu { get; set; } = [];
    public Order? ExistingOrder { get; set; }
    public bool CanEdit { get; set; }
    public Reservation? Booking { get; set; }
    [BindProperty] public decimal? ExpectedTotal { get; set; }
    private bool IsStaff => User.IsInRole("Staff") || User.IsInRole("Administrator");

    private async Task<Reservation?> LoadAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return null;
        var booking = await context.Reservations.SingleOrDefaultAsync(r => r.Id == Id);
        if (booking == null || (!IsStaff && booking.CustomerUserId != userId)) return null;
        Booking = booking;
        ExistingOrder = await context.Orders.Include(o => o.Items).ThenInclude(i => i.MenuItem)
            .SingleOrDefaultAsync(o => o.ReservationId == Id);
        Menu = await dining.Menu().AsNoTracking().Include(m => m.MenuCategory)
            .OrderBy(m => m.MenuCategoryId).ThenBy(m => m.Name).ToListAsync();
        CanEdit = booking.Status == ReservationStatus.Confirmed && booking.StartTime > ReservationRules.MalaysiaNow
            && (ExistingOrder == null || (ExistingOrder.Status == OrderStatus.AwaitingArrival && ExistingOrder.PaymentStatus == PaymentStatus.Unpaid));
        return booking;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() == null) return NotFound();
        if (ExistingOrder != null)
            Quantities = ExistingOrder.Items.Where(i => i.MenuItemId.HasValue).ToDictionary(i => i.MenuItemId!.Value, i => i.Quantity);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            await dining.LockAsync();
            var booking = await LoadAsync();
            if (booking == null) return NotFound();
            if (!CanEdit) { ModelState.AddModelError("", "This pre-order can no longer be changed."); return Page(); }
            if (!ModelState.IsValid) return Page();
            if (Quantities.Any(q => q.Value < 0 || q.Value > 99))
                ModelState.AddModelError("", "Enter quantities between 0 and 99.");
            var selected = Quantities.Where(q => q.Value > 0).ToList();
            if (selected.Count == 0) ModelState.AddModelError("", "Choose at least one dish, or go back to keep your reservation without a pre-order.");
            if (selected.Any(q => !Menu.Any(m => m.Id == q.Key)))
                ModelState.AddModelError("", "One of your dishes is no longer available. Please review your selection.");
            if (!ModelState.IsValid) return Page();
            var lines = OrderingMenu.BuildLines(Menu, Quantities);
            var currentTotal = lines.Sum(i => i.LineTotal);
            if (ExpectedTotal.HasValue && ExpectedTotal.Value != currentTotal)
            {
                ModelState.AddModelError("", "Menu prices have changed. Review your updated cart total before confirming again.");
                return Page();
            }
            var oldTotal = ExistingOrder?.TotalAmount;   // for the "Booking updated" email
            var order = ExistingOrder ?? new Order
            {
                ReservationId = Id, OrderType = OrderType.OnlinePreOrder,
                Status = OrderStatus.AwaitingArrival, OrderedAt = ReservationRules.MalaysiaNow,
                CustomerId = booking.CustomerUserId, CustomerName = booking.CustomerName,
                RestaurantTableId = booking.RestaurantTableId
            };
            if (ExistingOrder == null) context.Orders.Add(order);
            else { context.OrderItems.RemoveRange(order.Items); order.Items.Clear(); }
            foreach (var line in lines) order.Items.Add(line);
            order.TotalAmount = currentTotal;
            await emails.QueuePreOrderAsync(booking, oldTotal, order.TotalAmount, byCustomer: !IsStaff);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = "Pre-order saved. It is awaiting your arrival and has not been sent to the kitchen.";
            return RedirectToPage("Details", new { id = Id });
        }
        catch (Microsoft.Data.SqlClient.SqlException) { ModelState.AddModelError("", "Unable to save right now. Please reload and try again."); return Page(); }
        catch (DbUpdateException) { ModelState.AddModelError("", "Unable to save your pre-order. Please reload and review it before retrying."); return Page(); }
    }
}

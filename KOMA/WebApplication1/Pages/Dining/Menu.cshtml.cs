using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
namespace KOMA.Pages.Dining;

[AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MenuModel(DiningOrderService service, TabletAccess tablet, ApplicationDbContext db) : PageModel
{
    [BindProperty] public Dictionary<int,int> Quantities { get; set; } = [];
    [BindProperty] public decimal? ExpectedTotal { get; set; }
    [BindProperty] public int? ReviewedVisit { get; set; }
    [BindProperty] public int? Latest { get; set; }
    [BindProperty] public string? PreorderReview { get; set; }
    public TableServiceSession Visit { get; private set; } = null!;
    public List<MenuItem> Dishes { get; private set; } = [];
    public Order? Preorder { get; private set; }
    private string? customer;
    private async Task<bool> LoadAsync()
    {
        var grant = await tablet.VisitAsync(HttpContext);
        if (!grant.HasValue) return false;
        var (id, account) = grant.Value;
        customer = account;
        var visit = await service.ActiveVisits().Include(s => s.RestaurantTable).SingleOrDefaultAsync(s => s.Id == id);
        if (visit == null) return false;
        Visit = visit;
        Dishes = await service.Menu().AsNoTracking().Include(m => m.MenuCategory)
            .OrderBy(m => m.MenuCategory!.Name).ThenBy(m => m.Name).ToListAsync();
        Preorder = visit.ReservationId.HasValue ? await db.Orders.AsNoTracking().Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.ReservationId == visit.ReservationId && o.CustomerId == account && o.Status == OrderStatus.AwaitingArrival) : null;
        return true;
    }
    private async Task ReviewAsync()
    {
        Latest = await service.LatestOrderAsync(Visit.Id);
        PreorderReview = Preorder == null ? "" : OrderWorkflowService.ReviewToken(Preorder);
    }
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return RedirectToPage("Activate");
        await ReviewAsync();
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return RedirectToPage("Activate");
        if (ReviewedVisit != Visit.Id) return BadRequest("The table visit has changed.");
        if (ModelState.IsValid && Latest.HasValue && ExpectedTotal.HasValue)
        {
            try
            {
                var result = await service.PlaceAsync(Visit.Id, Visit.RestaurantTableId, Quantities, ExpectedTotal.Value, Latest.Value, PreorderReview, customer);
                if (result.Error == null)
                {
                    TempData["DiningNotice"] = "Your order has been sent to the kitchen.";
                    return RedirectToPage();
                }
                ModelState.AddModelError("", result.Error);
            }
            catch (Exception ex) when (ex is DbUpdateException or Microsoft.Data.SqlClient.SqlException)
            { ModelState.AddModelError("", "Could not confirm this order. Check with Staff before trying again."); }
        }
        else ModelState.AddModelError("", "Review the bill and quantities.");
        if (!await LoadAsync()) return RedirectToPage("Activate");
        await ReviewAsync();
        return Page();
    }
}

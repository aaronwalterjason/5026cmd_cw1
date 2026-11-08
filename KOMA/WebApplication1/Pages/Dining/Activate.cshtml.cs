using KOMA.Data;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
namespace KOMA.Pages.Dining;

[AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ActivateModel(TabletAccess tablet, DiningOrderService service, ApplicationDbContext db) : PageModel
{
    public string? TableNumber { get; private set; }
    public async Task<IActionResult> OnGetAsync()
    {
        var grant = await tablet.VisitAsync(HttpContext);
        if (grant.HasValue && await service.ActiveVisits().AnyAsync(s => s.Id == grant.Value.Visit))
            return RedirectToPage("Menu");
        var id = await tablet.TableAsync(HttpContext);
        TableNumber = await db.RestaurantTables.Where(t => t.Id == id).Select(t => t.TableNumber).SingleOrDefaultAsync();
        return Page();
    }
    public async Task<IActionResult> OnPostExitAsync(string? pin)
    {
        var error = tablet.CheckExitPin(await tablet.TableAsync(HttpContext), pin);
        if (error != null) return new ContentResult { StatusCode = 403, Content = error, ContentType = "text/plain" };
        var grant = await tablet.VisitAsync(HttpContext);
        if (grant.HasValue) await service.EndAsync(grant.Value.Visit);
        tablet.End(HttpContext);
        return RedirectToPage("/Account/Login", new { area = "Identity", returnUrl = Url.Page("/Staff/Index") });
    }
    public Task<IActionResult> OnPostGuestAsync() => StartAsync(null, false);
    public Task<IActionResult> OnPostScanAsync(string? code) => StartAsync(code, true);
    private async Task<IActionResult> StartAsync(string? code, bool scan)
    {
        var state = await OnGetAsync();
        if (state is RedirectToPageResult) return state;
        var id = await tablet.TableAsync(HttpContext);
        if (id == null) return StatusCode(403);
        var account = scan ? tablet.Redeem(code) : null;
        if (scan && account == null) { ModelState.AddModelError("", "This code expired or was used. Open a new QR code in your account."); return Page(); }
        var result = await service.BeginAsync(id.Value, account);
        if (!result.Id.HasValue) { ModelState.AddModelError("", result.Error!); return Page(); }
        tablet.Open(HttpContext, id.Value, result.Id.Value, account);
        return RedirectToPage("Menu");
    }
}

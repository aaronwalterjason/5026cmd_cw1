using System.Security.Claims;
using KOMA.Data;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Pages.OrderHistory;

[Authorize(Roles = "Customer"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DetailsModel(ApplicationDbContext db) : PageModel
{
    public DiningHistory Visit { get; private set; } = null!;
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public async Task<IActionResult> OnGetAsync(string? reference)
    {
        var customer = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (customer == null) return Challenge();
        var visits = await CustomerOrderHistory.LoadAsync(db, customer, ReservationRules.MalaysiaNow);
        var visit = visits.SingleOrDefault(v => v.Reference == reference);
        if (visit == null) return NotFound();
        Visit = visit;
        PageNumber = Math.Max(1, PageNumber);
        return Page();
    }
}

using System.Security.Claims;
using KOMA.Data;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Pages.OrderHistory;

[Authorize(Roles = "Customer"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(ApplicationDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public List<DiningHistory> Visits { get; private set; } = [];
    public int TotalPages { get; private set; }
    public async Task<IActionResult> OnGetAsync()
    {
        var customer = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (customer == null) return Challenge();
        var visits = await CustomerOrderHistory.LoadAsync(db, customer, ReservationRules.MalaysiaNow);
        TotalPages = Math.Max(1, (int)Math.Ceiling(visits.Count / 9d));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Visits = visits.Skip((PageNumber - 1) * 9).Take(9).ToList();
        return Page();
    }
}

using System.Security.Claims;
using KOMA.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Reservations;

[Authorize(Roles = "Customer,Staff,Administrator")]
public class IndexModel(ApplicationDbContext context) : PageModel
{
    public List<KOMA.Models.Reservation> Bookings { get; set; } = [];
    public HashSet<int> EditableIds { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.IsInRole("Staff") || User.IsInRole("Administrator"))
            return RedirectToPage("/Staff/Reservation/Index");
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Challenge();
        var now = KOMA.Services.ReservationRules.MalaysiaNow;
        var historyStart = now.Date.AddMonths(-3);
        Bookings = await context.Reservations.AsNoTracking().Include(r => r.RestaurantTable)
            .Where(r => r.CustomerUserId == userId && r.StartTime >= historyStart)
            .OrderByDescending(r => r.StartTime).ToListAsync();
        EditableIds = (await context.Reservations.Where(r => r.CustomerUserId == userId &&
            r.Status != KOMA.Models.ReservationStatus.Cancelled && r.StartTime > now &&
            !context.TableServiceSessions.Any(s => s.ReservationId == r.Id))
            .Select(r => r.Id).ToListAsync()).ToHashSet();
        return Page();
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Services;
using Microsoft.AspNetCore.Mvc;
using KOMA.Models;

namespace KOMA.Pages.Admin.Tables
{
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly DiningOrderService _dining;
        public IndexModel(ApplicationDbContext db, DiningOrderService dining)
        {
            _db = db;
            _dining = dining;
        }
        public List<TableServiceSession> OpenVisits { get; private set; } = [];

        public async Task<IActionResult> OnPostEndVisitAsync(int visitId, int tableId)
        {
            if (!ModelState.IsValid) return BadRequest("Invalid visit.");
            var error = await _dining.ReleaseAsync(visitId, tableId);
            if (error != null)
            {
                ModelState.AddModelError("", error);
                await OnGetAsync();
                return Page();
            }
            TempData["StatusMessage"] = "Visit ended. Table availability has been updated.";
            return RedirectToPage();
        }

        public IList<RestaurantTable> Tables { get; set; } = new List<RestaurantTable>();

        public async Task OnGetAsync()
        {
            OpenVisits = await _db.TableServiceSessions.AsNoTracking()
                .Where(s => !s.IsReleased && s.StartTime <= ReservationRules.MalaysiaNow)
                .OrderBy(s => s.StartTime).ToListAsync();
            // Archived tables are hidden (kept only for booking history).
            Tables = await _db.RestaurantTables.Where(t => !t.IsArchived).Include(t => t.DiningArea).ThenInclude(a => a!.FloorLayout).OrderBy(t => t.TableNumber).ToListAsync();
        }
    }
}

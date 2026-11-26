using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services;

namespace KOMA.Pages.Admin.Layouts
{
    [Authorize(Roles = "Administrator")]
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public DeleteModel(ApplicationDbContext db) => _db = db;

        public FloorLayout Floor { get; set; } = default!;
        public int AreaCount { get; set; }
        public int TableCount { get; set; }
        public string? Blocked { get; set; }   // tables with bookings or history -> can't delete the floor

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var floor = await _db.FloorLayouts.AsNoTracking()
                .Include(f => f.DiningAreas).ThenInclude(a => a.Tables.Where(t => !t.IsArchived))
                .FirstOrDefaultAsync(f => f.Id == id);
            if (floor is null) return NotFound();

            Floor = floor;
            AreaCount = floor.DiningAreas.Count;
            TableCount = floor.DiningAreas.Sum(a => a.Tables.Count);
            Blocked = await BlockedAsync(floor);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            // Dining areas (and their tables) on this floor are deleted with it - see ApplicationDbContext.
            // Only allowed when none of its tables (archived ones included) has ever been booked or used.
            var floor = await _db.FloorLayouts.FindAsync(id);
            if (floor is null) return RedirectToPage("./Index");
            if (await BlockedAsync(floor) is { } reason)
            {
                TempData["StatusError"] = reason;
                return RedirectToPage(new { id });
            }
            try
            {
                _db.FloorLayouts.Remove(floor);
                await _db.SaveChangesAsync();
                TempData["StatusMessage"] = $"'{floor.Name}' was deleted.";
            }
            catch (DbUpdateException)
            {
                TempData["StatusError"] = $"'{floor.Name}' is still in use, so it wasn't deleted.";
            }
            return RedirectToPage("./Index");
        }

        private async Task<string?> BlockedAsync(FloorLayout floor)
        {
            var areaIds = await _db.DiningAreas.Where(a => a.FloorLayoutId == floor.Id).Select(a => a.Id).ToListAsync();
            var usage = (await TableUsage.ForAreasAsync(_db, areaIds)).Values;
            return KOMA.Pages.Admin.DiningAreas.DeleteModel.Reason($"'{floor.Name}'", usage)?
                .Replace("Make it inactive instead.", "Make its dining areas inactive instead.");
        }
    }
}

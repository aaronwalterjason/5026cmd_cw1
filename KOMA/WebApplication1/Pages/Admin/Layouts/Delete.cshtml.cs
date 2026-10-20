using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;

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

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var floor = await _db.FloorLayouts.AsNoTracking()
                .Include(f => f.DiningAreas).ThenInclude(a => a.Tables)
                .FirstOrDefaultAsync(f => f.Id == id);
            if (floor is null) return NotFound();

            Floor = floor;
            AreaCount = floor.DiningAreas.Count;
            TableCount = floor.DiningAreas.Sum(a => a.Tables.Count);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            // Dining areas (and their tables) on this floor are deleted with it - see ApplicationDbContext.
            var floor = await _db.FloorLayouts.FindAsync(id);
            if (floor is not null)
            {
                _db.FloorLayouts.Remove(floor);
                await _db.SaveChangesAsync();
                TempData["StatusMessage"] = $"'{floor.Name}' was deleted.";
            }
            return RedirectToPage("./Index");
        }
    }
}

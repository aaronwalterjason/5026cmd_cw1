using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin.Layouts
{
    [Authorize(Roles = "Administrator")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public EditModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public FloorLayout Floor { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var floor = await _db.FloorLayouts.FindAsync(id);
            if (floor is null) return NotFound();
            Floor = floor;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            var existing = await _db.FloorLayouts.Include(f => f.DiningAreas).FirstOrDefaultAsync(f => f.Id == id);
            if (existing is null) return NotFound();

            if (await _db.FloorLayouts.AnyAsync(f => f.Name == Floor.Name && f.Id != id))
                ModelState.AddModelError("Floor.Name", "A floor with this name already exists.");

            // The floor can't be made smaller than the dining areas already drawn on it.
            var cutOff = existing.DiningAreas
                .Where(a => a.LayoutX + a.LayoutWidth > Floor.Width + 0.001 || a.LayoutY + a.LayoutHeight > Floor.Height + 0.001)
                .Select(a => a.Name).ToList();
            if (cutOff.Count > 0)
            {
                var needW = existing.DiningAreas.Max(a => a.LayoutX + a.LayoutWidth);
                var needH = existing.DiningAreas.Max(a => a.LayoutY + a.LayoutHeight);
                ModelState.AddModelError(string.Empty,
                    $"This size would cut off {string.Join(", ", cutOff)}. The floor must be at least {Math.Ceiling(needW)} m × {Math.Ceiling(needH)} m, " +
                    "or move those areas in the layout first.");
            }

            if (!ModelState.IsValid)
            {
                Floor.Id = id;
                return Page();
            }

            existing.Name = Floor.Name;
            existing.Level = Floor.Level;
            existing.Description = Floor.Description;
            existing.Width = Floor.Width;
            existing.Height = Floor.Height;
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"'{existing.Name}' was updated.";
            return RedirectToPage("./Index");
        }
    }
}

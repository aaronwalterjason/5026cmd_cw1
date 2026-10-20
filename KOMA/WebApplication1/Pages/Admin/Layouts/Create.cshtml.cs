using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin.Layouts
{
    [Authorize(Roles = "Administrator")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public CreateModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public FloorLayout Floor { get; set; } = new();

        public async Task OnGetAsync()
        {
            // Suggest the next level number.
            var levels = await _db.FloorLayouts.Select(f => f.Level).ToListAsync();
            Floor.Level = levels.Count == 0 ? 0 : levels.Max() + 1;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (await _db.FloorLayouts.AnyAsync(f => f.Name == Floor.Name))
                ModelState.AddModelError("Floor.Name", "A floor with this name already exists.");
            if (!ModelState.IsValid) return Page();

            _db.FloorLayouts.Add(Floor);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"'{Floor.Name}' was created. Now draw its dining areas.";
            return RedirectToPage("./Designer", new { id = Floor.Id });
        }
    }
}

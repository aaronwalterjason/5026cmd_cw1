using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin.DiningAreas
{
    [Authorize(Roles = "Administrator")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public EditModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public DiningArea DiningArea { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var area = await _db.DiningAreas.FindAsync(id);
            if (area is null) return NotFound();
            DiningArea = area;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Copy only the fields on this form, so the area's floor and layout position are kept.
            var existing = await _db.DiningAreas.FindAsync(DiningArea.Id);
            if (existing is null) return NotFound();
            existing.Name = DiningArea.Name;
            existing.Zone = DiningArea.Zone;
            existing.Description = DiningArea.Description;
            existing.IsActive = DiningArea.IsActive;
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"'{DiningArea.Name}' was updated.";
            return RedirectToPage("./Index");
        }
    }
}

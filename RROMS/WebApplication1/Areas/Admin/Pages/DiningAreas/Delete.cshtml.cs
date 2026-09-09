using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Data;
using RROMS.Models.Admin;

namespace RROMS.Areas.Admin.Pages.DiningAreas
{
    [Authorize(Roles = "Administrator")]
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public DeleteModel(ApplicationDbContext db) => _db = db;

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
            // TODO: block delete (or cascade with confirmation) if the area still has tables,
            // and check Engineer B's reservation table for a foreign-key reference first.
            var area = await _db.DiningAreas.FindAsync(DiningArea.Id);
            if (area is not null)
            {
                _db.DiningAreas.Remove(area);
                await _db.SaveChangesAsync();
            }

            TempData["StatusMessage"] = "Dining area was deleted.";
            return RedirectToPage("./Index");
        }
    }
}

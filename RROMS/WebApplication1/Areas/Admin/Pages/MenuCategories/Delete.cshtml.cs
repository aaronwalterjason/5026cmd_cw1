using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Data;
using RROMS.Models.Admin;

namespace RROMS.Areas.Admin.Pages.MenuCategories
{
    [Authorize(Roles = "Administrator")]
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public DeleteModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public MenuCategory Category { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var category = await _db.MenuCategories.FindAsync(id);
            if (category is null) return NotFound();
            Category = category;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // TODO: block delete (or reassign) if the category still has menu items.
            var category = await _db.MenuCategories.FindAsync(Category.Id);
            if (category is not null)
            {
                _db.MenuCategories.Remove(category);
                await _db.SaveChangesAsync();
            }

            TempData["StatusMessage"] = "Menu category was deleted.";
            return RedirectToPage("./Index");
        }
    }
}

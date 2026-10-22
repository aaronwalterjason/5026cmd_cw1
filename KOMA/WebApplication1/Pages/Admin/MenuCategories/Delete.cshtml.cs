using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using KOMA.Data;
using KOMA.Models;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Admin.MenuCategories
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
            var category = await _db.MenuCategories.FindAsync(Category.Id);
            if (category is not null)
            {
                Category = category;
                if (await _db.MenuItems.AnyAsync(i => i.MenuCategoryId == category.Id))
                {
                    ModelState.AddModelError("", "This category still contains dishes, including unavailable dishes kept for order history. Move those dishes to another category, or mark this category inactive instead.");
                    return Page();
                }
                _db.MenuCategories.Remove(category);
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    ModelState.AddModelError("", "The category could not be deleted. Reload and check whether any dishes still use it.");
                    return Page();
                }
            }

            TempData["StatusMessage"] = "Menu category was deleted.";
            return RedirectToPage("./Index");
        }
    }
}

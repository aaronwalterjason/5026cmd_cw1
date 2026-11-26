using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin.MenuCategories
{
    [Authorize(Roles = "Administrator")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public EditModel(ApplicationDbContext db) => _db = db;

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
            Category.Name = (Category.Name ?? "").Trim();   // category names are unique (case-insensitive)
            if (Category.Name.Length > 0 && await _db.MenuCategories.AnyAsync(c => c.Id != Category.Id && c.Name == Category.Name))
                ModelState.AddModelError("Category.Name", $"A category called '{Category.Name}' already exists.");
            if (!ModelState.IsValid)
            {
                return Page();
            }

            _db.MenuCategories.Update(Category);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"'{Category.Name}' was updated.";
            return RedirectToPage("./Index");
        }
    }
}

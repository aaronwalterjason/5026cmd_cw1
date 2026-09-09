using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models.Admin;

namespace RROMS.Areas.Admin.Pages.MenuItems
{
    [Authorize(Roles = "Administrator")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public EditModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public MenuItem Item { get; set; } = new();

        public SelectList CategoryOptions { get; set; } = null!;

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var item = await _db.MenuItems.FindAsync(id);
            if (item is null) return NotFound();
            Item = item;
            await LoadOptionsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadOptionsAsync();
                return Page();
            }

            _db.MenuItems.Update(Item);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"'{Item.Name}' was updated.";
            return RedirectToPage("./Index");
        }

        private async Task LoadOptionsAsync()
        {
            var categories = await _db.MenuCategories.OrderBy(c => c.DisplayOrder).ToListAsync();
            CategoryOptions = new SelectList(categories, nameof(MenuCategory.Id), nameof(MenuCategory.Name), Item.MenuCategoryId);
        }
    }
}

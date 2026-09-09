using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Data;
using RROMS.Models.Admin;

namespace RROMS.Areas.Admin.Pages.MenuItems
{
    [Authorize(Roles = "Administrator")]
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public DeleteModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public MenuItem Item { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var item = await _db.MenuItems.FindAsync(id);
            if (item is null) return NotFound();
            Item = item;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var item = await _db.MenuItems.FindAsync(Item.Id);
            if (item is not null)
            {
                _db.MenuItems.Remove(item);
                await _db.SaveChangesAsync();
            }

            TempData["StatusMessage"] = "Menu item was deleted.";
            return RedirectToPage("./Index");
        }
    }
}

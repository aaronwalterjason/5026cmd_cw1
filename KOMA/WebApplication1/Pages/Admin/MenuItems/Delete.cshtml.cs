using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin.MenuItems
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
            if (item is null) return RedirectToPage("./Index");

            // Past orders keep their own copy of the dish name and price (OrderItem.ItemName / UnitPrice),
            // so the dish can be removed; their link to it is cleared by the database (SetNull).
            _db.MenuItems.Remove(item);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = "Menu item was deleted.";
            return RedirectToPage("./Index");
        }
    }
}

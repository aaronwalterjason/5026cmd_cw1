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

            // A dish that appears on past orders must stay in the database so order history and
            // revenue reports remain correct - hide it from customers instead of deleting it.
            if (await _db.OrderItems.AnyAsync(oi => oi.MenuItemId == item.Id))
            {
                item.IsAvailable = false;
                await _db.SaveChangesAsync();
                TempData["StatusMessage"] = $"'{item.Name}' appears on past orders, so it was marked unavailable instead of deleted.";
                return RedirectToPage("./Index");
            }

            _db.MenuItems.Remove(item);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = "Menu item was deleted.";
            return RedirectToPage("./Index");
        }
    }
}

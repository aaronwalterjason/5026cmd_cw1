using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Data;
using RROMS.Models.Admin;

namespace RROMS.Areas.Admin.Pages.Tables
{
    [Authorize(Roles = "Administrator")]
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public DeleteModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public RestaurantTable Table { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var table = await _db.RestaurantTables.FindAsync(id);
            if (table is null) return NotFound();
            Table = table;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var table = await _db.RestaurantTables.FindAsync(Table.Id);
            if (table is not null)
            {
                _db.RestaurantTables.Remove(table);
                await _db.SaveChangesAsync();
            }

            TempData["StatusMessage"] = "Table was deleted.";
            return RedirectToPage("./Index");
        }
    }
}

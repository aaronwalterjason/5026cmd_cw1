using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models.Admin;

namespace RROMS.Areas.Admin.Pages.Tables
{
    [Authorize(Roles = "Administrator")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public EditModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public RestaurantTable Table { get; set; } = new();

        public SelectList DiningAreaOptions { get; set; } = null!;

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var table = await _db.RestaurantTables.FindAsync(id);
            if (table is null) return NotFound();
            Table = table;
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

            _db.RestaurantTables.Update(Table);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"Table '{Table.TableNumber}' was updated.";
            return RedirectToPage("./Index");
        }

        private async Task LoadOptionsAsync()
        {
            var areas = await _db.DiningAreas.OrderBy(a => a.Name).ToListAsync();
            DiningAreaOptions = new SelectList(areas, nameof(DiningArea.Id), nameof(DiningArea.Name), Table.DiningAreaId);
        }
    }
}

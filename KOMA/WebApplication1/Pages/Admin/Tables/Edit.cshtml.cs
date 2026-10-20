using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin.Tables
{
    [Authorize(Roles = "Administrator")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public EditModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public RestaurantTable Table { get; set; } = new();

        public SelectList DiningAreaOptions { get; set; } = null!;
        public string AreaName { get; set; } = "";

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

            // Copy only the fields on this form, so the table's dining area and layout position are kept.
            var existing = await _db.RestaurantTables.FindAsync(Table.Id);
            if (existing is null) return NotFound();
            existing.TableNumber = Table.TableNumber;
            existing.Capacity = Table.Capacity;
            existing.Shape = Table.Shape;
            existing.IsActive = Table.IsActive;
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"Table '{Table.TableNumber}' was updated.";
            return RedirectToPage("./Index");
        }

        private async Task LoadOptionsAsync()
        {
            var areas = await _db.DiningAreas.OrderBy(a => a.Name).ToListAsync();
            DiningAreaOptions = new SelectList(areas, nameof(DiningArea.Id), nameof(DiningArea.Name), Table.DiningAreaId);
            var id = Table.DiningAreaId;
            if (id == 0 && Table.Id != 0)
                id = await _db.RestaurantTables.Where(t => t.Id == Table.Id).Select(t => t.DiningAreaId).FirstOrDefaultAsync();
            AreaName = areas.FirstOrDefault(a => a.Id == id)?.Name ?? "";
        }
    }
}

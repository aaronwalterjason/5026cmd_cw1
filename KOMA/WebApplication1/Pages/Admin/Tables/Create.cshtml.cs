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
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public CreateModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public RestaurantTable Table { get; set; } = new();

        public SelectList DiningAreaOptions { get; set; } = null!;

        public async Task OnGetAsync()
        {
            await LoadOptionsAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadOptionsAsync();
                return Page();
            }

            _db.RestaurantTables.Add(Table);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"Table '{Table.TableNumber}' was created.";
            return RedirectToPage("./Index");
        }

        private async Task LoadOptionsAsync()
        {
            var areas = await _db.DiningAreas.OrderBy(a => a.Name).ToListAsync();
            DiningAreaOptions = new SelectList(areas, nameof(DiningArea.Id), nameof(DiningArea.Name));
        }
    }
}

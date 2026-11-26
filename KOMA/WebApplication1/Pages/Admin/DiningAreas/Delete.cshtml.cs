using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services;

namespace KOMA.Pages.Admin.DiningAreas
{
    [Authorize(Roles = "Administrator")]
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public DeleteModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public DiningArea DiningArea { get; set; } = new();
        // Why it can't be deleted (tables with bookings or history), or null if it can.
        public string? Blocked { get; set; }
        public int TableCount { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var area = await _db.DiningAreas.FindAsync(id);
            if (area is null) return NotFound();
            DiningArea = area;
            TableCount = await _db.RestaurantTables.CountAsync(t => t.DiningAreaId == id && !t.IsArchived);
            Blocked = await BlockedAsync(area);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Its tables are deleted with it, so this is only allowed when none of them (archived ones included)
            // has ever been booked or used. Otherwise the admin makes the area inactive instead.
            var area = await _db.DiningAreas.FindAsync(DiningArea.Id);
            if (area is null) return RedirectToPage("./Index");
            if (await BlockedAsync(area) is { } reason)
            {
                TempData["StatusError"] = reason;
                return RedirectToPage(new { id = area.Id });
            }
            try
            {
                _db.DiningAreas.Remove(area);
                await _db.SaveChangesAsync();
                TempData["StatusMessage"] = $"'{area.Name}' was deleted.";
            }
            catch (DbUpdateException)
            {
                TempData["StatusError"] = $"'{area.Name}' is still in use, so it wasn't deleted. Make it inactive instead.";
            }
            return RedirectToPage("./Index");
        }

        internal static string? Reason(string what, IEnumerable<TableUsage.Info> usage)
        {
            var list = usage.ToList();
            if (list.FirstOrDefault(u => TableUsage.BlockReason(u) is not null) is { } busy)
                return $"{what} can't be deleted: {TableUsage.BlockReason(busy)}";
            if (list.Any(u => u.HasHistory))
                return $"{what} has tables with booking history, so it can't be deleted (past bookings, bills and reports need them). Make it inactive instead.";
            return null;
        }

        private async Task<string?> BlockedAsync(DiningArea area) =>
            Reason($"'{area.Name}'", (await TableUsage.ForAreasAsync(_db, [area.Id])).Values);
    }
}

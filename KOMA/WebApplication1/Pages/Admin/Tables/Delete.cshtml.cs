using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services;

namespace KOMA.Pages.Admin.Tables
{
    // Remove a table safely (see Services/TableUsage.cs):
    //   upcoming bookings or guests seated -> blocked; "Move upcoming bookings" moves them to other tables first
    //   only past history                  -> ARCHIVED (hidden, kept for bookings, bills and reports)
    //   never used                         -> deleted
    // The Move handler is also used by the floor designer (with a returnUrl back to the designer).
    [Authorize(Roles = "Administrator")]
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly ReservationService _reservations;
        public DeleteModel(ApplicationDbContext db, ReservationService reservations) { _db = db; _reservations = reservations; }

        [BindProperty]
        public RestaurantTable Table { get; set; } = new();
        public TableUsage.Info Usage { get; set; } = default!;

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var table = await _db.RestaurantTables.Include(t => t.DiningArea).FirstOrDefaultAsync(t => t.Id == id && !t.IsArchived);
            if (table is null) return NotFound();
            Table = table;
            Usage = (await TableUsage.ForTablesAsync(_db, [id]))[id];
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var table = await _db.RestaurantTables.FirstOrDefaultAsync(t => t.Id == Table.Id && !t.IsArchived);
            if (table is null) return RedirectToPage("./Index");
            var usage = (await TableUsage.ForTablesAsync(_db, [table.Id]))[table.Id];

            if (TableUsage.BlockReason(usage) is { } reason)
            {
                TempData["StatusError"] = reason;
                return RedirectToPage(new { id = table.Id });
            }
            try
            {
                if (usage.HasHistory)
                {
                    TableUsage.Archive(table);
                    await _db.SaveChangesAsync();
                    TempData["StatusTitle"] = "Table archived";
                    TempData["StatusMessage"] = $"Table {table.TableNumber} has booking history, so it was archived: hidden and never bookable, but kept for past bookings, bills and reports.";
                }
                else
                {
                    _db.RestaurantTables.Remove(table);
                    await _db.SaveChangesAsync();
                    TempData["StatusMessage"] = $"Table {table.TableNumber} was deleted.";
                }
            }
            catch (DbUpdateException)
            {
                // Safety net: something started using the table in the meantime.
                TempData["StatusError"] = $"Table {table.TableNumber} is still in use, so it wasn't removed. Make it inactive instead.";
            }
            return RedirectToPage("./Index");
        }

        // "Move upcoming bookings": every upcoming booking on this table goes to another free table that fits.
        public async Task<IActionResult> OnPostMoveBookingsAsync(int id, string? returnUrl)
        {
            var table = await _db.RestaurantTables.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (table is null) return NotFound();
            var result = await _reservations.MoveOffTableAsync(id, User);
            if (result.Moved.Count > 0)
            {
                TempData["StatusTitle"] = "Bookings moved";
                TempData["StatusMessage"] = $"{result.Moved.Count} booking{(result.Moved.Count == 1 ? "" : "s")} moved off table {table.TableNumber}: "
                    + string.Join("; ", result.Moved) + ". Customers were emailed the new table.";
            }
            if (result.NotMoved.Count > 0)
            {
                TempData["StatusNoteTitle"] = result.Moved.Count > 0 ? "Some bookings weren't moved" : "Bookings not moved";
                TempData["StatusError"] = string.Join("; ", result.NotMoved) + ". Contact these customers or choose a table for them by hand.";
            }
            if (result.Moved.Count == 0 && result.NotMoved.Count == 0)
                TempData["StatusMessage"] = $"Table {table.TableNumber} has no upcoming bookings to move.";
            return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToPage(new { id });
        }
    }
}

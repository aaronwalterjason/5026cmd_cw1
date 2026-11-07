using System.Security.Claims;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Reservations;

[Authorize(Roles = "Customer,Staff,Administrator")]
public class DetailsModel(ApplicationDbContext context, ReservationService reservations) : PageModel
{
    public KOMA.Models.Reservation Booking { get; set; } = null!;
    public bool IsStaff => User.IsInRole("Staff") || User.IsInRole("Administrator");
    public string BackPage => IsStaff ? "/Staff/Reservation/Index" : "/Reservations/Index";
    public bool CanChange => Booking.Status != ReservationStatus.Cancelled && Booking.StartTime > ReservationRules.MalaysiaNow;
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var booking = await context.Reservations.AsNoTracking().Include(r => r.RestaurantTable).ThenInclude(t => t.DiningArea).SingleOrDefaultAsync(r => r.Id == id);
        if (booking == null || (!IsStaff && booking.CustomerUserId != User.FindFirstValue(ClaimTypes.NameIdentifier))) return NotFound();
        Booking = booking;
        return Page();
    }
    public Task<IActionResult> OnPostConfirmAsync(int id, string rowVersion) => ChangeAsync(id, rowVersion, true);
    public async Task<IActionResult> OnPostCancelAsync(int id, string rowVersion, bool confirmCancel)
    {
        if (!confirmCancel) { TempData["ErrorMessage"] = "Tick the cancellation confirmation before cancelling."; return RedirectToPage(new { id }); }
        return await ChangeAsync(id, rowVersion, false);
    }
    private async Task<IActionResult> ChangeAsync(int id, string rowVersion, bool confirm)
    {
        try
        {
            var error = await reservations.ChangeStatusAsync(id, rowVersion, confirm, User);
            TempData[error == null ? "SuccessMessage" : "ErrorMessage"] = error ?? (confirm ? "Reservation confirmed." : "Reservation cancelled. The table is available again.");
        }
        catch (Microsoft.Data.SqlClient.SqlException) { TempData["ErrorMessage"] = "The database is busy or unavailable. Check the current status before trying again."; }
        catch (DbUpdateException) { TempData["ErrorMessage"] = "The booking could not be updated. Review the current details and try again."; }
        return RedirectToPage(new { id });
    }
}


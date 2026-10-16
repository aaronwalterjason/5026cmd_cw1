using KOMA.Data;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Staff.Reservations;

[Authorize(Roles = "Staff,Administrator")]
public class EditModel(ApplicationDbContext context, ReservationService reservations) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public ReservationInput Input { get; set; } = new();
    public List<SelectListItem> Tables { get; set; } = [];
    public IReadOnlyList<TimeOnly> Slots { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(int? tableId, DateOnly? date, TimeOnly? time)
    {
        await LoadChoicesAsync();
        if (Id.HasValue)
        {
            var booking = await context.Reservations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == Id.Value);
            if (booking == null) return NotFound();
            Input = new ReservationInput
            {
                RestaurantTableId = booking.RestaurantTableId,
                CustomerName = booking.CustomerName,
                ContactNumber = booking.ContactNumber,
                Email = booking.Email,
                Date = DateOnly.FromDateTime(booking.StartTime),
                Time = TimeOnly.FromDateTime(booking.StartTime),
                GuestCount = booking.GuestCount,
                SpecialRequests = booking.SpecialRequests,
                RowVersion = Convert.ToBase64String(booking.RowVersion)
            };
        }
        else
        {
            Input.RestaurantTableId = tableId ?? 0;
            Input.Date = date ?? DateOnly.FromDateTime(ReservationRules.MalaysiaNow);
            Input.Time = time ?? Slots.FirstOrDefault();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadChoicesAsync();
        if (!ModelState.IsValid) return Page();
        try
        {
            var result = await reservations.SaveAsync(Input, Id, User);
            if (result.Error != null) { ModelState.AddModelError("", result.Error); return Page(); }
            TempData["SuccessMessage"] = Id.HasValue ? "Reservation updated successfully." : "Reservation created. It is pending confirmation.";
            return RedirectToPage("Details", new { id = result.Id });
        }
        catch (Microsoft.Data.SqlClient.SqlException) { ModelState.AddModelError("", "The database is busy or unavailable. Please retry; check the reservation list before submitting again."); return Page(); }
        catch (DbUpdateException) { ModelState.AddModelError("", "The reservation could not be saved. Reload the page and check table availability."); return Page(); }
    }

    private async Task LoadChoicesAsync()
    {
        Tables = await context.RestaurantTables.AsNoTracking().Where(t => t.IsActive && t.DiningArea!.IsActive)
            .OrderBy(t => t.DiningAreaId).ThenBy(t => t.TableNumber)
            .Select(t => new SelectListItem($"{t.DiningArea!.Name} / {t.TableNumber} — capacity {t.Capacity}", t.Id.ToString())).ToListAsync();
        var settings = await context.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync();
        if (settings != null) Slots = ReservationRules.Slots(settings);
    }
}


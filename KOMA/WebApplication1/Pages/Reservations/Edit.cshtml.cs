using System.Security.Claims;
using KOMA.Data;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Reservations;

[Authorize(Roles = "Customer,Staff,Administrator")]
public class EditModel(ApplicationDbContext context, ReservationService reservations) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public ReservationInput Input { get; set; } = new();
    [BindProperty] public bool AddPreOrder { get; set; }
    public bool IsStaff => User.IsInRole("Staff") || User.IsInRole("Administrator");
    public string BackPage => IsStaff ? "/Staff/Reservation/Index" : "/Reservations/Index";
    public List<SelectListItem> Tables { get; set; } = [];
    public List<SelectListItem> Zones { get; set; } = [];
    public string? SuggestedZone { get; set; }
    public string? SuggestionMessage { get; set; }
    public IReadOnlyList<TimeOnly> Slots { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(int? tableId, DateOnly? date, TimeOnly? time)
    {
        await LoadChoicesAsync();
        if (Id.HasValue)
        {
            var booking = await context.Reservations.AsNoTracking().Include(r => r.RestaurantTable).ThenInclude(t => t.DiningArea).SingleOrDefaultAsync(r => r.Id == Id.Value);
            if (booking == null || (!IsStaff && booking.CustomerUserId != User.FindFirstValue(ClaimTypes.NameIdentifier))) return NotFound();
            if (!IsStaff && (booking.Status == KOMA.Models.ReservationStatus.Cancelled ||
                booking.StartTime <= ReservationRules.MalaysiaNow ||
                await context.TableServiceSessions.AnyAsync(s => s.ReservationId == booking.Id)))
            {
                TempData["ErrorMessage"] = "This reservation can no longer be edited. Please contact staff.";
                return RedirectToPage("Details", new { id = booking.Id });
            }
            Input = new ReservationInput
            {
                RestaurantTableId = booking.RestaurantTableId,
                PreferredZone = booking.RestaurantTable.DiningArea?.Zone,
                CustomerName = booking.CustomerName,
                ContactNumber = booking.ContactNumber,
                Email = booking.Email,
                Date = DateOnly.FromDateTime(booking.StartTime),
                Time = TimeOnly.FromDateTime(booking.StartTime),
                GuestCount = booking.GuestCount,
                SpecialRequests = booking.SpecialRequests,
                RowVersion = Convert.ToBase64String(booking.RowVersion)
            };
            // Keep the saved values visible even if restaurant choices changed after booking.
            Slots = Slots.Append(Input.Time).Distinct().OrderBy(t => t).ToList();
            if (Input.PreferredZone != null && !Zones.Any(z => z.Value == Input.PreferredZone))
                Zones.Add(new SelectListItem(Input.PreferredZone, Input.PreferredZone));
            if (!Tables.Any(t => t.Value == Input.RestaurantTableId.ToString()))
                Tables.Add(new SelectListItem($"{booking.RestaurantTable.DiningArea?.Name} / {booking.RestaurantTable.TableNumber}", Input.RestaurantTableId.ToString()));
        }
        else
        {
            Input.RestaurantTableId = tableId ?? 0;
            Input.Date = date ?? DateOnly.FromDateTime(ReservationRules.MalaysiaNow);
            Input.Time = time ?? Slots.FirstOrDefault();
            if (!IsStaff)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var customer = await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId);
                if (customer == null) return Challenge();
                Input.CustomerName = customer.FullName ?? "";
                Input.ContactNumber = customer.PhoneNumber ?? "";
                Input.Email = customer.Email;
            }
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? acceptedZone)
    {
        await LoadChoicesAsync();
        if (!ModelState.IsValid) return Page();
        if (!IsStaff && !string.IsNullOrWhiteSpace(acceptedZone))
        {
            Input.PreferredZone = acceptedZone;
            ModelState.Remove("Input.PreferredZone");
        }
        try
        {
            var result = await reservations.SaveAsync(Input, Id, User);
            if (result.SuggestedZone != null)
            {
                SuggestedZone = result.SuggestedZone;
                SuggestionMessage = result.Error;
                return Page();
            }
            if (result.Error != null) { ModelState.AddModelError("", result.Error); return Page(); }
            TempData["SuccessMessage"] = Id.HasValue ? "Reservation updated successfully." : "Reservation confirmed. Your table is reserved.";
            return RedirectToPage(AddPreOrder ? "PreOrder" : "Details", new { id = result.Id });
        }
        catch (Microsoft.Data.SqlClient.SqlException) { ModelState.AddModelError("", "The database is busy or unavailable. Please retry; check the reservation list before submitting again."); return Page(); }
        catch (DbUpdateException) { ModelState.AddModelError("", "The reservation could not be saved. Reload the page and check table availability."); return Page(); }
    }

    public async Task<IActionResult> OnGetSlotsAsync(DateOnly date, int guests, int? table, string? zone)
    {
        if (date == default || date.Year > 9998 || guests < 1 || guests > 20) return new JsonResult(Array.Empty<string>());
        var own = Id.HasValue ? await context.Reservations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == Id) : null;
        if (own != null && !IsStaff && own.CustomerUserId != User.FindFirstValue(ClaimTypes.NameIdentifier)) return Forbid();
        var settings = await context.RestaurantSettings.FirstAsync();
        var result = new List<string>();
        foreach (var slot in ReservationRules.Slots(settings, date))   // none on closed days / closed dates
        {
            var start = date.ToDateTime(slot);
            var choices = ReservationService.AvailableTables(context, guests, own?.Id, start, start, start.AddMinutes(ReservationRules.SlotMinutes));
            if (IsStaff && table.HasValue) choices = choices.Where(t => t.Id == table);
            if (!IsStaff && !string.IsNullOrWhiteSpace(zone)) choices = choices.Where(t => t.DiningArea!.Zone == zone);
            if ((start > ReservationRules.MalaysiaNow && await choices.AnyAsync()) || start == own?.StartTime)
                result.Add(slot.ToString("HH:mm"));
        }
        if (own != null && DateOnly.FromDateTime(own.StartTime) == date)
            result.Add(own.StartTime.ToString("HH:mm"));
        return new JsonResult(result.Distinct().OrderBy(t => t).ToList());
    }

    private async Task LoadChoicesAsync()
    {
        Zones = await context.DiningAreas.AsNoTracking().Where(a => a.IsActive)
            .Select(a => a.Zone).Distinct().OrderBy(z => z)
            .Select(z => new SelectListItem(z, z)).ToListAsync();
        Tables = await context.RestaurantTables.AsNoTracking().Where(t => t.IsActive && t.DiningArea!.IsActive)
            .OrderBy(t => t.DiningAreaId).ThenBy(t => t.TableNumber)
            .Select(t => new SelectListItem($"{t.DiningArea!.Name} / {t.TableNumber} — capacity {t.Capacity}", t.Id.ToString())).ToListAsync();
        var settings = await context.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync();
        if (settings != null) Slots = ReservationRules.Slots(settings);
    }
}

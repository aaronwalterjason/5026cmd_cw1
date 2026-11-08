using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages;
[AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class FeedbackModel(ApplicationDbContext db, TabletAccess tablet, DiningOrderService dining) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? ReservationId { get; set; }
    [BindProperty(SupportsGet = true)] public bool Tablet { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool Visitor => !Tablet && ReservationId == null && User.Identity?.IsAuthenticated != true;
    public bool Submitted { get; private set; }
    private Feedback? existing;
    private int? visitId;
    private string? account;
    private string name = "Visitor";
    private string? email;
    public class InputModel
    {
        [Range(1, 5, ErrorMessage = "Choose a rating from 1 to 5 stars.")] public int Rating { get; set; }
        [Required, StringLength(2000)] public string Comment { get; set; } = "";
        [StringLength(100)] public string? Name { get; set; }
        [EmailAddress, StringLength(256)] public string? Email { get; set; }
    }
    private async Task<bool> LoadAsync()
    {
        account = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Tablet)
        {
            var grant = await tablet.VisitAsync(HttpContext);
            if (!grant.HasValue) return false;
            var visit = await dining.ActiveVisits().SingleOrDefaultAsync(v => v.Id == grant.Value.Visit);
            if (visit == null) return false;
            visitId = visit.Id;
            ReservationId = visit.ReservationId;
            account = grant.Value.Customer;
            name = visit.CustomerName;
        }
        else if (ReservationId.HasValue)
        {
            var booking = await db.Reservations.SingleOrDefaultAsync(r => r.Id == ReservationId && r.CustomerUserId == account && account != null);
            if (booking == null || booking.Status != ReservationStatus.Confirmed || booking.EndTime > ReservationRules.MalaysiaNow) return false;
            var visit = await db.TableServiceSessions.SingleOrDefaultAsync(v => v.ReservationId == booking.Id);
            if (visit == null || visit.EndTime > ReservationRules.MalaysiaNow) return false;
            visitId = visit.Id;
            name = booking.CustomerName;
            email = booking.Email;
        }
        if (account != null)
        {
            var user = await db.Users.FindAsync(account);
            name = user?.FullName ?? name;
            email = user?.Email ?? email;
        }
        if (visitId != null || ReservationId != null)
            existing = await db.Feedbacks.SingleOrDefaultAsync(f => (visitId != null && f.TableServiceSessionId == visitId) || (ReservationId != null && f.ReservationId == ReservationId));
        return true;
    }
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return StatusCode(403);
        if (existing != null) Input = new() { Rating = existing.Rating, Comment = existing.Comment };
        Submitted = TempData["FeedbackSaved"] as bool? == true;
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return StatusCode(403);
        if (string.IsNullOrWhiteSpace(Input.Comment)) ModelState.AddModelError("Input.Comment", "Please enter your feedback.");
        if (!ModelState.IsValid) return Page();
        var feedback = existing ?? new Feedback { ReservationId = ReservationId, TableServiceSessionId = visitId, CustomerUserId = account, Source = Tablet ? "Tablet" : ReservationId.HasValue ? "Reservation" : "Website" };
        feedback.Rating = Input.Rating;
        feedback.Comment = Input.Comment.Trim();
        feedback.Name = Visitor ? (string.IsNullOrWhiteSpace(Input.Name) ? "Visitor" : Input.Name.Trim()) : name;
        feedback.Email = Visitor ? Input.Email?.Trim() : email;
        if (existing == null) db.Feedbacks.Add(feedback); else feedback.UpdatedAtUtc = DateTime.UtcNow;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { ModelState.AddModelError("", "Feedback could not be saved. Refresh this form and try again."); return Page(); }
        TempData["FeedbackSaved"] = true;
        return RedirectToPage(new { ReservationId, Tablet });
    }
}

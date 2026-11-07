using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using KOMA.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KOMA.Pages.Admin.Activity;

// Admin > Activity > Emails: every booking email - sent, waiting, failed or skipped - with Resend and View.
[Authorize(Roles = "Administrator")]
public class EmailsModel(ApplicationDbContext db, IOptionsMonitor<EmailOptions> options, IDataProtectionProvider dp) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }   // sent | waiting | failed | skipped
    [BindProperty(SupportsGet = true)] public int Days { get; set; } = 7;    // 1, 7, 30, 0 = all time

    public List<BookingEmail> Emails { get; private set; } = new();
    public Dictionary<BookingEmailStatus, int> Counts { get; private set; } = new();
    public int Total => Counts.Values.Sum();
    public BookingEmailStatus? StatusFilter => Enum.TryParse<BookingEmailStatus>(Status, true, out var s) ? s : null;

    private IQueryable<BookingEmail> InRange()
    {
        var q = db.BookingEmails.AsNoTracking();
        if (Days > 0) { var from = ReservationRules.MalaysiaNow.Date.AddDays(1 - Days); q = q.Where(e => e.CreatedAt >= from); }
        return q;
    }

    public async Task OnGetAsync()
    {
        Counts = await InRange().GroupBy(e => e.Status).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        var q = InRange();
        if (StatusFilter is { } s) q = q.Where(e => e.Status == s);
        Emails = await q.Include(e => e.Reservation).OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Take(200).ToListAsync();
    }

    // Queue it again (after fixing the address on the booking, for example). The worker re-reads the booking.
    public async Task<IActionResult> OnPostResendAsync(int id)
    {
        var e = await db.BookingEmails.FindAsync(id);
        if (e is null) return NotFound();
        e.Status = BookingEmailStatus.Waiting; e.Attempts = 0; e.Note = null; e.SendAfter = ReservationRules.MalaysiaNow;
        await db.SaveChangesAsync();
        TempData["StatusMessage"] = "The email will be sent again in the next minute.";
        return RedirectToPage(new { Status, Days });
    }

    // The email as the customer got it (or a preview of how it would look now).
    public async Task<IActionResult> OnGetViewAsync(int id)
    {
        var e = await db.BookingEmails.AsNoTracking()
            .Include(x => x.Reservation!).ThenInclude(r => r.RestaurantTable!).ThenInclude(t => t.DiningArea)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        if (e.Html is not null) return Content(e.Html, "text/html; charset=utf-8");
        if (e.Reservation is null) return Content("This booking no longer exists.", "text/plain");
        var pre = await db.Orders.AsNoTracking().Include(o => o.Items).Where(o => o.ReservationId == e.ReservationId).OrderByDescending(o => o.Id).FirstOrDefaultAsync();
        var o = options.CurrentValue;
        var off = e.Reservation.CustomerUserId is null ? null : $"{o.SiteUrl.TrimEnd('/')}/Notifications/Reminders?t=" +
            Uri.EscapeDataString(dp.CreateProtector(BookingEmailWorker.RemindersPurpose).Protect(e.Reservation.CustomerUserId));
        return Content(BookingEmailTemplates.Build(e, e.Reservation, pre, o, off).Html, "text/html; charset=utf-8");
    }
}

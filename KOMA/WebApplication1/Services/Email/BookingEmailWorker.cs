using KOMA.Data;
using KOMA.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KOMA.Services.Email;

// Runs in the background every 30 s:
//   1. creates reminder emails for confirmed bookings starting within EmailOptions.ReminderHours
//   2. sends waiting emails whose wait is over - re-reading the booking first (skips cancelled / moved ones),
//      retrying up to 3 times before marking Failed.
public class BookingEmailWorker(IServiceScopeFactory scopes, ILogger<BookingEmailWorker> log) : BackgroundService
{
    public const int MaxAttempts = 3;
    public const string RemindersPurpose = "KOMA.ReminderEmails";

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stop);   // let the app (and migrations) start first
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var o = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<EmailOptions>>().CurrentValue;
                await CreateRemindersAsync(db, o, stop);
                await SendDueAsync(scope.ServiceProvider, db, o, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogWarning(ex, "Booking email worker: {Message}", ex.Message); }   // e.g. table not created yet
            await Task.Delay(TimeSpan.FromSeconds(30), stop);
        }
    }

    public static string ReminderKey(Reservation r) => $"remind:{r.Id}:{r.StartTime:yyyyMMddHHmm}";

    private static async Task CreateRemindersAsync(ApplicationDbContext db, EmailOptions o, CancellationToken ct)
    {
        if (o.ReminderHours <= 0) return;
        var now = ReservationRules.MalaysiaNow;
        var due = await db.Reservations.AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Confirmed && r.StartTime > now.AddHours(2) && r.StartTime <= now.AddHours(o.ReminderHours)
                        && r.CreatedAtUtc < DateTime.UtcNow.AddHours(-o.ReminderHours / 2.0)   // booked at the last minute: no reminder needed
                        && (r.CustomerUser == null || !r.CustomerUser.NoReminderEmails))
            .Select(r => new { r.Id, r.StartTime, r.CustomerName }).ToListAsync(ct);
        foreach (var r in due)
        {
            var key = $"remind:{r.Id}:{r.StartTime:yyyyMMddHHmm}";
            if (await db.BookingEmails.AnyAsync(e => e.DedupKey == key, ct)) continue;
            db.BookingEmails.Add(new BookingEmail
            {
                ReservationId = r.Id, Type = BookingEmailType.Reminder, DedupKey = key, ToName = r.CustomerName,
                CreatedAt = now, SendAfter = now
            });
        }
        if (db.ChangeTracker.HasChanges())
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); }   // another instance made it first (unique DedupKey)
        }
    }

    private async Task SendDueAsync(IServiceProvider sp, ApplicationDbContext db, EmailOptions o, CancellationToken ct)
    {
        var now = ReservationRules.MalaysiaNow;
        var due = await db.BookingEmails
            .Include(e => e.Reservation!).ThenInclude(r => r.RestaurantTable!).ThenInclude(t => t.DiningArea)
            .Include(e => e.Reservation!).ThenInclude(r => r.CustomerUser)
            .Where(e => e.Status == BookingEmailStatus.Waiting && e.SendAfter <= now)
            .OrderBy(e => e.SendAfter).Take(20).ToListAsync(ct);
        if (due.Count == 0) return;

        var sender = sp.GetRequiredService<IMailSender>();
        var protector = sp.GetRequiredService<IDataProtectionProvider>().CreateProtector(RemindersPurpose);
        foreach (var e in due)
        {
            var r = e.Reservation;
            string? skip = r is null ? "Booking no longer exists"
                : e.Type == BookingEmailType.Reminder && (r.Status != ReservationStatus.Confirmed || e.DedupKey != ReminderKey(r)) ? "Booking was cancelled or moved"
                : e.Type is BookingEmailType.Confirmed or BookingEmailType.Updated && r.Status == ReservationStatus.Cancelled ? "Booking was cancelled"
                : null;
            var to = r?.Email ?? r?.CustomerUser?.Email;   // re-read now, so a corrected address is used on Resend
            if (skip is null && string.IsNullOrWhiteSpace(to)) skip = "No email on booking";
            if (skip is not null)
            {
                e.Status = BookingEmailStatus.Skipped; e.Note = skip;
                await db.SaveChangesAsync(ct);
                continue;
            }

            try
            {
                var preOrder = await db.Orders.AsNoTracking().Include(x => x.Items)
                    .Where(x => x.ReservationId == r!.Id && (x.Status == OrderStatus.AwaitingArrival || e.Type == BookingEmailType.Cancelled))
                    .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
                var off = r!.CustomerUserId is null ? null
                    : $"{o.SiteUrl.TrimEnd('/')}/Notifications/Reminders?t={Uri.EscapeDataString(protector.Protect(r.CustomerUserId))}";
                var built = BookingEmailTemplates.Build(e, r, preOrder, o, off);
                await sender.SendAsync(to!, r.CustomerName, built.Subject, built.Html, built.Text, built.Attachments, ct);
                e.Status = BookingEmailStatus.Sent; e.SentAt = ReservationRules.MalaysiaNow; e.ToEmail = to;
                e.Subject = built.Subject; e.Html = built.Html; e.Note = null; e.Attempts++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                e.Attempts++; e.ToEmail = to;
                e.Note = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
                if (e.Attempts >= MaxAttempts) e.Status = BookingEmailStatus.Failed;
                else e.SendAfter = ReservationRules.MalaysiaNow.AddMinutes(2 * e.Attempts);   // 2 min, then 4 min
                log.LogWarning("Booking email {Id} failed (attempt {N}): {Message}", e.Id, e.Attempts, ex.Message);
            }
            await db.SaveChangesAsync(ct);
        }
    }
}

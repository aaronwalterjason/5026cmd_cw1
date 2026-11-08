using System.Text.Json;
using KOMA.Data;
using KOMA.Models;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Services.Email;

// Queues booking emails. Call it inside the booking's own transaction, before SaveChanges, so the
// booking change and its email are saved together (or not at all). It never sends anything itself.
public class BookingEmailService(ApplicationDbContext db)
{
    public static readonly TimeSpan ConfirmWait = TimeSpan.FromMinutes(2);   // a pre-order made straight away joins the Confirmed email
    public static readonly TimeSpan UpdateWait = TimeSpan.FromMinutes(5);    // quick edits become one Updated email
    private static DateTime Now => ReservationRules.MalaysiaNow;

    private Task<List<BookingEmail>> WaitingAsync(Reservation r) => r.Id == 0
        ? Task.FromResult(new List<BookingEmail>())
        : db.BookingEmails.Where(e => e.ReservationId == r.Id && e.Status == BookingEmailStatus.Waiting).ToListAsync();

    private BookingEmail Add(Reservation r, BookingEmailType type, TimeSpan wait, string? by = null)
    {
        var e = new BookingEmail
        {
            Reservation = r, Type = type, By = by, ToName = r.CustomerName,
            CreatedAt = Now, SendAfter = Now + wait
        };
        db.BookingEmails.Add(e);
        return e;
    }

    // New booking, or staff confirmed a pending one.
    public async Task QueueConfirmedAsync(Reservation r)
    {
        if ((await WaitingAsync(r)).Any(e => e.Type == BookingEmailType.Confirmed)) return;
        Add(r, BookingEmailType.Confirmed, ConfirmWait);
    }

    // changes: label -> (old, new), e.g. "Time" -> ("7:00 pm", "8:00 pm")
    public async Task QueueUpdatedAsync(Reservation r, IDictionary<string, (string Old, string New)> changes, bool byCustomer)
    {
        if (changes.Count == 0) return;
        var waiting = await WaitingAsync(r);
        if (waiting.Any(e => e.Type == BookingEmailType.Confirmed)) return;   // the Confirmed email will show the latest details

        var e = waiting.FirstOrDefault(x => x.Type == BookingEmailType.Updated);
        var merged = e?.ChangesJson is null ? new Dictionary<string, string[]>()
            : JsonSerializer.Deserialize<Dictionary<string, string[]>>(e.ChangesJson) ?? new();
        foreach (var (label, (oldV, newV)) in changes)
            merged[label] = merged.TryGetValue(label, out var prev) ? [prev[0], newV] : [oldV, newV];   // keep the first "old"
        foreach (var k in merged.Where(kv => kv.Value[0] == kv.Value[1]).Select(kv => kv.Key).ToList()) merged.Remove(k); // changed back

        if (e is null)
        {
            if (merged.Count == 0) return;
            e = Add(r, BookingEmailType.Updated, UpdateWait, byCustomer ? "customer" : "staff");
        }
        else
        {
            e.SendAfter = Now + UpdateWait;
            if (!byCustomer) e.By = "staff";
            if (merged.Count == 0) { e.Status = BookingEmailStatus.Skipped; e.Note = "Changes were undone before sending"; }
        }
        e.ChangesJson = JsonSerializer.Serialize(merged);
    }

    public Task QueuePreOrderAsync(Reservation r, decimal? oldTotal, decimal newTotal, bool byCustomer) =>
        QueueUpdatedAsync(r, new Dictionary<string, (string, string)>
        {
            ["Pre-order"] = (oldTotal is null ? "None" : $"RM {oldTotal:0.00}", $"RM {newTotal:0.00}")
        }, byCustomer);

    public async Task QueueCancelledAsync(Reservation r, bool byCustomer)
    {
        var waiting = await WaitingAsync(r);
        foreach (var e in waiting)
        {
            e.Status = BookingEmailStatus.Skipped;
            e.Note = "Booking was cancelled before this was sent";
        }
        // Only tell them if they heard about the booking (or it was made before emails existed).
        var history = await db.BookingEmails.Where(e => e.ReservationId == r.Id).Select(e => e.Status).ToListAsync();
        var cancelled = Add(r, BookingEmailType.Cancelled, TimeSpan.Zero, byCustomer ? "customer" : "staff");
        if (history.Count > 0 && !history.Contains(BookingEmailStatus.Sent))
        {
            cancelled.Status = BookingEmailStatus.Skipped;
            cancelled.Note = "No earlier email reached the customer";
        }
    }
}

using System.ComponentModel.DataAnnotations;

namespace KOMA.Models
{
    // Owned by Engineer B. One email to a customer about their booking (the "outbox").
    // Rows are added inside the same transaction as the booking change; Services/Email/BookingEmailWorker sends them.
    // Shown to admins on Admin > Activity > Emails.
    public class BookingEmail
    {
        public int Id { get; set; }

        public int? ReservationId { get; set; }
        public Reservation? Reservation { get; set; }

        public BookingEmailType Type { get; set; }
        public BookingEmailStatus Status { get; set; } = BookingEmailStatus.Waiting;

        [StringLength(256)] public string? ToEmail { get; set; }    // filled in when sent (the address is re-read from the booking)
        [StringLength(100)] public string ToName { get; set; } = "";

        public DateTime CreatedAt { get; set; }                     // Malaysia time
        public DateTime SendAfter { get; set; }                     // waits a little so quick edits become one email
        public DateTime? SentAt { get; set; }
        public int Attempts { get; set; }

        [StringLength(300)] public string? Note { get; set; }       // why it was skipped / the last error

        [StringLength(120)] public string? DedupKey { get; set; }   // reminders: remind:{id}:{start} - never sent twice
        [StringLength(20)] public string? By { get; set; }          // "customer" or "staff" (who updated / cancelled)
        public string? ChangesJson { get; set; }                    // Updated: { "Time": ["7:00 pm", "8:00 pm"], ... }

        [StringLength(200)] public string? Subject { get; set; }    // as sent
        public string? Html { get; set; }                           // as sent, for "View" in the email log
    }

    public enum BookingEmailType { Confirmed = 0, Updated = 1, Cancelled = 2, Reminder = 3, ConfirmEmail = 4, PasswordReset = 5 }   // 4-5: account emails (no booking)
    public enum BookingEmailStatus { Waiting = 0, Sent = 1, Failed = 2, Skipped = 3 }
}

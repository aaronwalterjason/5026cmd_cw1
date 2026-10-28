using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace KOMA.Models
{
    // Owned by Engineer B. One entry in Admin > Activity: a change made by an admin or staff member, or a sign-in.
    // Written automatically by Services/AuditInterceptor (menu, bookings, floors, settings), by the Accounts page
    // (role changes) and by Services/AuditSignInManager (sign-ins). Keeps before/after values so menu and settings
    // changes can be reverted.
    public class ActivityLog
    {
        public int Id { get; set; }
        public DateTime At { get; set; } = DateTime.Now;

        // Who did it (copied, so the log stays readable if the account changes later).
        public string? UserId { get; set; }
        [Required, StringLength(100)] public string UserName { get; set; } = "";
        [StringLength(20)] public string? UserRole { get; set; }          // "Admin" or "Staff"

        [Required, StringLength(30)] public string Area { get; set; } = Areas.Menu;   // Areas.* (the tabs)
        [Required, StringLength(40)] public string Action { get; set; } = "Edited";  // short, e.g. "Price changed"
        [Required, StringLength(150)] public string Title { get; set; } = "";       // what it was about, e.g. "Dragon Roll"
        [StringLength(300)] public string? Summary { get; set; }                    // e.g. "RM 32.00 → RM 34.00"

        // JSON list of ActivitySnapshot: each row that changed, with values before and after.
        public string? SnapshotsJson { get; set; }

        // Revert links: this entry undid RevertOfId / this entry was undone by RevertedById.
        public int? RevertOfId { get; set; }
        public int? RevertedById { get; set; }

        public List<ActivitySnapshot> Snapshots() =>
            string.IsNullOrEmpty(SnapshotsJson) ? new() : JsonSerializer.Deserialize<List<ActivitySnapshot>>(SnapshotsJson) ?? new();

        public static class Areas
        {
            public const string Menu = "Menu", Bookings = "Bookings", Floors = "Floors", Settings = "Settings", SignIns = "Sign-ins";
        }
    }

    // One database row that changed in a log entry.
    public class ActivitySnapshot
    {
        public string Type { get; set; } = "";       // EF entity type name, e.g. "KOMA.Models.MenuItem"
        public string Label { get; set; } = "";      // e.g. "Dragon Roll", "Table T5"
        public string State { get; set; } = "";      // Added | Modified | Deleted
        public Dictionary<string, JsonElement> Key { get; set; } = new();
        public Dictionary<string, JsonElement>? Before { get; set; }
        public Dictionary<string, JsonElement>? After { get; set; }
        public List<string> Changed { get; set; } = new();
    }
}

using System.ComponentModel.DataAnnotations;

namespace KOMA.Models
{
    // Advanced-tier feature. Owned by Engineer A; schedule, banner and home-page fields added for the Admin > Promotions page.
    public class Promotion
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Title { get; set; } = string.Empty;

        [Range(0, 100)]
        public decimal DiscountPercent { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsActive { get; set; } = true;

        // Which weekdays it runs on: bit 0 = Monday ... bit 6 = Sunday (127 = every day, 31 = Mon-Fri).
        public int Days { get; set; } = 127;

        // Time of day it runs; both empty = all day.
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }

        // Home page banner (landing page "This week at KOMA").
        [StringLength(160)] public string? BannerText { get; set; }
        [StringLength(300)] public string? BannerImageUrl { get; set; }
        public bool ShowOnHome { get; set; }

        public ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();

        // ---------- Helpers (not stored) ----------
        public static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        public static int DayIndex(DayOfWeek d) => ((int)d + 6) % 7;      // Monday = 0
        public bool RunsOn(int dayIndex) => (Days & (1 << dayIndex)) != 0;
        public bool AllDay => StartTime is null || EndTime is null;

        // Live (running right now) | Today (starts later today) | Scheduled | Paused | Ended
        public string StatusAt(DateTime now)
        {
            if (EndDate.Date < now.Date) return "Ended";
            if (!IsActive) return "Paused";
            if (StartDate.Date > now.Date || !RunsOn(DayIndex(now.DayOfWeek))) return "Scheduled";
            if (AllDay) return "Live";
            var t = TimeOnly.FromDateTime(now);
            if (t < StartTime) return "Today";
            return t <= EndTime ? "Live" : (EndDate.Date > now.Date ? "Scheduled" : "Ended");
        }

        // Does the discount apply to food served at this time? (date range, weekday, time window, not paused)
        // Applies only to dine-in orders placed on the table tablet, at the time they are placed (pre-orders pay the menu price).
        public bool AppliesAt(DateTime when)
        {
            if (!IsActive || DiscountPercent <= 0) return false;
            if (when.Date < StartDate.Date || when.Date > EndDate.Date) return false;
            if (!RunsOn(DayIndex(when.DayOfWeek))) return false;
            if (AllDay) return true;
            var t = TimeOnly.FromDateTime(when);
            return t >= StartTime!.Value && t <= EndTime!.Value;
        }

        public string DaysText()
        {
            if (Days == 127) return "Every day";
            if (Days == 31) return "Mon–Fri";
            if (Days == 96) return "Sat–Sun";
            return string.Join(", ", Enumerable.Range(0, 7).Where(RunsOn).Select(i => DayNames[i]));
        }

        public static string Clock(TimeOnly t) => t.ToString(t.Minute == 0 ? "h tt" : "h:mm tt").ToLowerInvariant();
        public string TimeText() => AllDay ? "All day" : $"{Clock(StartTime!.Value)} – {Clock(EndTime!.Value)}";
        public string ScheduleText() => AllDay ? DaysText() : $"{DaysText()} · {TimeText()}";
    }
}

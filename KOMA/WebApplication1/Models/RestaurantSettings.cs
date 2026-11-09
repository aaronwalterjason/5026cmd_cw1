using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;

namespace KOMA.Models
{
    // Single-row table holding restaurant-wide configuration, edited in Admin > Settings.
    //   WeeklyHours        - Monday..Sunday separated by '|', each "HH:mm-HH:mm" or "closed".
    //                        Empty = open every day from OpeningTime to ClosingTime (rows saved before per-day hours).
    //   ClosedDates        - JSON list of { From, To, Reason } for holidays and events.
    //   LastBookingMinutes - bookings stop this many minutes before closing (75 = 1-hour slot + 15-min turnaround).
    // OpeningTime / ClosingTime are kept as the earliest opening and latest closing of the week for older code.
    public class RestaurantSettings
    {
        public int Id { get; set; }

        public TimeOnly OpeningTime { get; set; } = new(10, 0);

        public TimeOnly ClosingTime { get; set; } = new(22, 0);

        [StringLength(300)]
        public string? AnnouncementText { get; set; }

        [StringLength(120)]
        public string WeeklyHours { get; set; } = "";

        [StringLength(4000)]
        public string? ClosedDates { get; set; }

        // Rows saved before this column existed read as 0 -> treated as the default 75.
        [Range(60, 240)]
        public int LastBookingMinutes { get => _lastBooking < 60 ? 75 : _lastBooking; set => _lastBooking = value; }
        private int _lastBooking = 75;

        // ---------- Helpers (not stored) ----------
        public record DayHours(bool IsOpen, TimeOnly Opens, TimeOnly Closes);
        public record ClosedPeriod(DateOnly From, DateOnly To, string Reason)
        {
            public bool Covers(DateOnly d) => d >= From && d <= To;
            public string Label => From == To ? $"{From:ddd d MMM yyyy}"
                : From.Year == To.Year && From.Month == To.Month ? $"{From:ddd d} – {To:ddd d MMM yyyy}"
                : $"{From:ddd d MMM} – {To:ddd d MMM yyyy}";
        }
        // What one calendar date looks like: open or not, its hours, and why it is closed (holiday / event).
        public record DayPlan(bool IsOpen, TimeOnly Opens, TimeOnly Closes, string? ClosedReason);

        public static readonly string[] DayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        public static int DayIndex(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;      // Monday = 0

        public IReadOnlyList<DayHours> Week()
        {
            var parts = (WeeklyHours ?? "").Split('|');
            var week = new List<DayHours>(7);
            for (var i = 0; i < 7; i++) week.Add(i < parts.Length ? Parse(parts[i]) : new DayHours(true, OpeningTime, ClosingTime));
            return week;
        }

        private DayHours Parse(string part)
        {
            part = part.Trim();
            if (part.Equals("closed", StringComparison.OrdinalIgnoreCase)) return new DayHours(false, OpeningTime, ClosingTime);
            var bits = part.Split('-');
            if (bits.Length == 2
                && TimeOnly.TryParseExact(bits[0], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var opens)
                && TimeOnly.TryParseExact(bits[1], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var closes))
                return new DayHours(true, opens, closes);
            return new DayHours(true, OpeningTime, ClosingTime);
        }

        public static string Format(IEnumerable<DayHours> week) => string.Join("|", week.Select(d => d.IsOpen
            ? $"{d.Opens.ToString("HH:mm", CultureInfo.InvariantCulture)}-{d.Closes.ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : "closed"));

        public List<ClosedPeriod> Closures()
        {
            if (string.IsNullOrWhiteSpace(ClosedDates)) return new();
            try { return (JsonSerializer.Deserialize<List<ClosedPeriod>>(ClosedDates) ?? new()).OrderBy(c => c.From).ToList(); }
            catch (JsonException) { return new(); }
        }

        public void SetClosures(IEnumerable<ClosedPeriod> list)
        {
            var sorted = list.OrderBy(c => c.From).ToList();
            ClosedDates = sorted.Count == 0 ? null : JsonSerializer.Serialize(sorted);
        }

        public DayPlan For(DateOnly date)
        {
            var closure = Closures().FirstOrDefault(c => c.Covers(date));
            var day = Week()[DayIndex(date)];
            if (closure is not null) return new DayPlan(false, day.Opens, day.Closes, closure.Reason);
            return new DayPlan(day.IsOpen, day.Opens, day.Closes, null);
        }

        // Latest booking start on a day, e.g. closes 22:00 with 75 minutes -> 20:45.
        public TimeOnly LastBookingFor(TimeOnly closes) =>
            TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Math.Max(0, closes.ToTimeSpan().TotalMinutes - LastBookingMinutes)));

        // "Mon closed · Tue–Thu 11:00–22:00 · Fri 11:00–23:00" (used by Admin > Activity).
        public static string Describe(string? weeklyHours, TimeOnly opening, TimeOnly closing)
        {
            var week = new RestaurantSettings { WeeklyHours = weeklyHours ?? "", OpeningTime = opening, ClosingTime = closing }.Week();
            string Text(DayHours d) => d.IsOpen ? $"{d.Opens:HH:mm}–{d.Closes:HH:mm}" : "closed";
            var parts = new List<string>();
            for (var i = 0; i < 7;)
            {
                var j = i;
                while (j + 1 < 7 && Text(week[j + 1]) == Text(week[i])) j++;
                var days = i == j ? DayNames[i][..3] : $"{DayNames[i][..3]}–{DayNames[j][..3]}";
                parts.Add($"{days} {Text(week[i])}");
                i = j + 1;
            }
            return string.Join(" · ", parts);
        }

        public static string DescribeClosures(string? json)
        {
            var list = new RestaurantSettings { ClosedDates = json }.Closures();
            return list.Count == 0 ? "None" : string.Join(" · ", list.Select(c => $"{(c.From == c.To ? c.From.ToString("d MMM yyyy") : $"{c.From:d MMM}–{c.To:d MMM yyyy}")} {c.Reason}"));
        }
    }
}

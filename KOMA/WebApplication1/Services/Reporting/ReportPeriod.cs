using System.Globalization;

namespace KOMA.Services.Reporting
{
    // One reporting period: a day, a week (Monday to Sunday) or a calendar month.
    // Read from the query string: ?period=day|week|month&date=yyyy-MM-dd&month=yyyy-MM
    public sealed class ReportPeriod
    {
        public string Kind { get; }               // "day" | "week" | "month"
        public DateTime Start { get; }
        public DateTime End { get; }              // exclusive
        public DateTime Last => End.AddDays(-1);
        public string Label { get; }              // "Mon 9 Nov 2026" | "9–15 Nov 2026" | "November 2026"
        public string FileTag { get; }            // "2026-11-09" | "week-2026-11-09" | "2026-11"
        public bool HasNext { get; }
        public bool InProgress { get; }

        public Dictionary<string, string> Route { get; }      // this period
        public Dictionary<string, string> PrevRoute { get; }
        public Dictionary<string, string> NextRoute { get; }

        public string? DateValue => Kind == "month" ? null : Start.ToString("yyyy-MM-dd");
        public string MonthValue => Start.ToString("yyyy-MM");
        public string LongLabel => Kind switch { "week" => "Week of " + Label, _ => Label };

        public ReportPeriod(string? kind, DateTime? date, string? month, DateTime now)
        {
            var today = now.Date;
            Kind = kind?.ToLowerInvariant() switch { "day" => "day", "week" => "week", _ => "month" };

            switch (Kind)
            {
                case "day":
                    Start = (date ?? today).Date;
                    End = Start.AddDays(1);
                    Label = Start.ToString("ddd d MMM yyyy");
                    FileTag = Start.ToString("yyyy-MM-dd");
                    Route = R("day", Start);
                    PrevRoute = R("day", Start.AddDays(-1));
                    NextRoute = R("day", End);
                    break;

                case "week":
                    Start = WeekStart(date ?? today);
                    End = Start.AddDays(7);
                    Label = Span(Start, End.AddDays(-1));
                    FileTag = "week-" + Start.ToString("yyyy-MM-dd");
                    Route = R("week", Start);
                    PrevRoute = R("week", Start.AddDays(-7));
                    NextRoute = R("week", End);
                    break;

                default:
                    if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
                        m = date ?? today;
                    Start = new DateTime(m.Year, m.Month, 1);
                    End = Start.AddMonths(1);
                    Label = Start.ToString("MMMM yyyy");
                    FileTag = Start.ToString("yyyy-MM");
                    Route = new() { ["period"] = "month", ["month"] = Start.ToString("yyyy-MM") };
                    PrevRoute = new() { ["period"] = "month", ["month"] = Start.AddMonths(-1).ToString("yyyy-MM") };
                    NextRoute = new() { ["period"] = "month", ["month"] = End.ToString("yyyy-MM") };
                    break;
            }
            HasNext = End <= today;
            InProgress = Start <= now && now < End;
        }

        // The same kind of period, for today (used by the Day / Week / Month switch).
        public static Dictionary<string, string> TodayRoute(string kind, DateTime anchor) => kind switch
        {
            "day" => R("day", anchor),
            "week" => R("week", WeekStart(anchor)),
            _ => new() { ["period"] = "month", ["month"] = anchor.ToString("yyyy-MM") }
        };

        private static Dictionary<string, string> R(string kind, DateTime d) =>
            new() { ["period"] = kind, ["date"] = d.ToString("yyyy-MM-dd") };

        public static DateTime WeekStart(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

        // "9–15 Nov 2026", "30 Nov – 6 Dec 2026", "29 Dec 2025 – 4 Jan 2026"
        public static string Span(DateTime first, DateTime last) =>
            first.Year != last.Year ? $"{first:d MMM yyyy} – {last:d MMM yyyy}"
            : first.Month != last.Month ? $"{first:d MMM} – {last:d MMM yyyy}"
            : first.Day == last.Day ? first.ToString("d MMM yyyy")
            : $"{first.Day}–{last:d MMM yyyy}";
    }
}

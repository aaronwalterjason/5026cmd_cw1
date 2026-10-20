using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin.Revenue
{
    // Total sales for one day, month or year. Only PAID orders count, on the date they were paid.
    // A period that is still running (today, this month, this year) is compared with the SAME LENGTH
    // of the previous period - e.g. 1-16 Oct against 1-16 Sep - so a half-finished month isn't
    // unfairly compared with a whole one.
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        // ---- Inputs, e.g. ?period=month&month=2026-10 ----
        [BindProperty(SupportsGet = true)] public string Period { get; set; } = "month";  // day | month | year
        [BindProperty(SupportsGet = true)] public DateTime? Date { get; set; }           // for "day"
        [BindProperty(SupportsGet = true)] public string? Month { get; set; }            // "yyyy-MM", for "month"
        [BindProperty(SupportsGet = true)] public int? Year { get; set; }                // for "year"

        // ---- The period shown ----
        public DateTime Now { get; private set; }
        public DateTime Start { get; private set; }
        public DateTime End { get; private set; }            // exclusive
        public bool InProgress { get; private set; }         // the period includes "now"
        public string PeriodLabel { get; private set; } = "";   // title, e.g. "October 2026"
        public string SpanLabel { get; private set; } = "";     // card label, e.g. "1–16 OCT"
        public string CompareLabel { get; private set; } = "";  // e.g. "1–16 Sep"

        // ---- Totals, this period and the comparison period ----
        public decimal TotalRevenue { get; private set; }
        public int PaidOrders { get; private set; }
        public int ItemsSold { get; private set; }
        public decimal AverageOrderValue => PaidOrders == 0 ? 0 : TotalRevenue / PaidOrders;
        public decimal PrevRevenue { get; private set; }
        public int PrevOrders { get; private set; }
        public decimal PrevAverage { get; private set; }

        // ---- Chart ----
        public List<Bucket> Breakdown { get; private set; } = new();
        public string BreakdownTitle { get; private set; } = "";
        public decimal AxisMax { get; private set; }             // top gridline
        public decimal AveragePerBucket { get; private set; }     // dashed line
        public string BucketWord { get; private set; } = "day";  // "hour" | "day" | "month"

        // ---- Toolbar ----
        public Dictionary<string, string> PrevRoute { get; private set; } = new();
        public Dictionary<string, string> NextRoute { get; private set; } = new();
        public bool HasNext { get; private set; }
        public List<int> YearOptions { get; private set; } = new();

        public record Bucket(string Label, string FullLabel, decimal Amount, int Orders, bool IsCurrent, bool IsFuture);

        public async Task OnGetAsync()
        {
            Now = DateTime.Now;
            var today = DateTime.Today;
            Period = Period?.ToLowerInvariant() switch { "day" => "day", "year" => "year", _ => "month" };

            DateTime prevStart, prevFullEnd, nextStart;
            switch (Period)
            {
                case "year":
                    var y = Year ?? today.Year;
                    Year = y;
                    Start = new DateTime(y, 1, 1);
                    End = Start.AddYears(1);
                    prevStart = Start.AddYears(-1); prevFullEnd = Start; nextStart = End;
                    PeriodLabel = y.ToString();
                    PrevRoute = new() { ["period"] = "year", ["year"] = (y - 1).ToString() };
                    NextRoute = new() { ["period"] = "year", ["year"] = (y + 1).ToString() };
                    break;

                case "day":
                    var d = (Date ?? today).Date;
                    Date = d;
                    Start = d;
                    End = d.AddDays(1);
                    prevStart = d.AddDays(-7); prevFullEnd = prevStart.AddDays(1); nextStart = End;   // same weekday last week
                    PeriodLabel = d.ToString("ddd d MMMM yyyy");
                    PrevRoute = new() { ["period"] = "day", ["date"] = d.AddDays(-1).ToString("yyyy-MM-dd") };
                    NextRoute = new() { ["period"] = "day", ["date"] = d.AddDays(1).ToString("yyyy-MM-dd") };
                    break;

                default:
                    if (!DateTime.TryParseExact(Month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
                        m = new DateTime(today.Year, today.Month, 1);
                    Start = new DateTime(m.Year, m.Month, 1);
                    End = Start.AddMonths(1);
                    Month = Start.ToString("yyyy-MM");
                    prevStart = Start.AddMonths(-1); prevFullEnd = Start; nextStart = End;
                    PeriodLabel = Start.ToString("MMMM yyyy");
                    PrevRoute = new() { ["period"] = "month", ["month"] = prevStart.ToString("yyyy-MM") };
                    NextRoute = new() { ["period"] = "month", ["month"] = End.ToString("yyyy-MM") };
                    break;
            }
            HasNext = nextStart <= today;

            // Fair comparison: a running period is compared with the same length of the previous one.
            InProgress = Start <= Now && Now < End;
            var compareEnd = InProgress ? Min(prevStart + (Now - Start), prevFullEnd) : prevFullEnd;
            var lastShown = (InProgress ? Now : End.AddTicks(-1));
            var lastCompared = compareEnd.AddTicks(-1);
            (SpanLabel, CompareLabel) = Period switch
            {
                "day" => (Start.ToString("ddd d MMM").ToUpperInvariant(),
                          InProgress ? $"last {prevStart:dddd} by {Now:h:mm tt}" : $"last {prevStart:dddd} ({prevStart:d MMM})"),
                "year" => (InProgress ? $"1 JAN – {lastShown:d MMM}".ToUpperInvariant() : Start.Year.ToString(),
                           InProgress ? $"1 Jan – {lastCompared:d MMM yyyy}" : prevStart.Year.ToString()),
                _ => (InProgress ? $"{DayRange(Start, lastShown)} {Start:MMM}".ToUpperInvariant() : Start.ToString("MMMM").ToUpperInvariant(),
                      InProgress ? $"{DayRange(prevStart, lastCompared)} {prevStart:MMM}" : prevStart.ToString("MMMM yyyy"))
            };

            var paid = _db.Orders.AsNoTracking().Where(o => o.PaymentStatus == PaymentStatus.Paid && o.PaidAt != null);

            // One period is small, so load it and group in C# (simple and database-independent).
            var orders = await paid
                .Where(o => o.PaidAt >= Start && o.PaidAt < End)
                .Include(o => o.Items)
                .ToListAsync();
            var prev = await paid
                .Where(o => o.PaidAt >= prevStart && o.PaidAt < compareEnd)
                .Select(o => o.TotalAmount)
                .ToListAsync();

            TotalRevenue = orders.Sum(o => o.TotalAmount);
            PaidOrders = orders.Count;
            ItemsSold = orders.Sum(o => o.Items.Sum(i => i.Quantity));
            PrevRevenue = prev.Sum();
            PrevOrders = prev.Count;
            PrevAverage = PrevOrders == 0 ? 0 : PrevRevenue / PrevOrders;

            BuildBreakdown(orders);

            var firstPaid = await paid.MinAsync(o => o.PaidAt);
            var firstYear = Math.Min(firstPaid?.Year ?? today.Year, Year ?? today.Year);
            YearOptions = Enumerable.Range(firstYear, today.Year - firstYear + 1).Reverse().ToList();
        }

        private void BuildBreakdown(List<Order> orders)
        {
            switch (Period)
            {
                case "year":
                    BreakdownTitle = "Sales by month"; BucketWord = "month";
                    Breakdown = Enumerable.Range(1, 12).Select(mo =>
                    {
                        var s = new DateTime(Start.Year, mo, 1);
                        var inMonth = orders.Where(o => o.PaidAt!.Value.Month == mo).ToList();
                        return new Bucket(s.ToString("MMM"), s.ToString("MMMM yyyy"), inMonth.Sum(o => o.TotalAmount), inMonth.Count,
                            Now >= s && Now < s.AddMonths(1), s > Now);
                    }).ToList();
                    break;

                case "day":
                    BreakdownTitle = "Sales by hour"; BucketWord = "hour";
                    var hours = orders.Select(o => o.PaidAt!.Value.Hour).ToList();
                    var first = Math.Min(10, hours.DefaultIfEmpty(10).Min());
                    var last = Math.Max(23, hours.DefaultIfEmpty(23).Max());
                    Breakdown = Enumerable.Range(first, last - first + 1).Select(h =>
                    {
                        var s = Start.AddHours(h);
                        var inHour = orders.Where(o => o.PaidAt!.Value.Hour == h).ToList();
                        return new Bucket(s.ToString("h tt"), $"{s:h tt}–{s.AddHours(1):h tt}", inHour.Sum(o => o.TotalAmount), inHour.Count,
                            Now >= s && Now < s.AddHours(1), s > Now);
                    }).ToList();
                    break;

                default:
                    BreakdownTitle = "Sales by day"; BucketWord = "day";
                    Breakdown = Enumerable.Range(1, DateTime.DaysInMonth(Start.Year, Start.Month)).Select(day =>
                    {
                        var s = Start.AddDays(day - 1);
                        var inDay = orders.Where(o => o.PaidAt!.Value.Day == day).ToList();
                        var showLabel = day == 1 || day % 5 == 0 || s.Date == Now.Date;
                        return new Bucket(showLabel ? day.ToString() : "", s.ToString("ddd d MMM"), inDay.Sum(o => o.TotalAmount), inDay.Count,
                            s.Date == Now.Date, s.Date > Now.Date);
                    }).ToList();
                    break;
            }

            // Average over the buckets that have already happened (not the future ones).
            var elapsed = Math.Max(1, Breakdown.Count(b => !b.IsFuture));
            AveragePerBucket = TotalRevenue / elapsed;

            // A "nice" top value for the gridlines, e.g. 180 -> 180, 172.5 -> 180, 950 -> 1200.
            var top = Math.Max(Breakdown.Select(b => b.Amount).DefaultIfEmpty(0).Max(), AveragePerBucket);
            AxisMax = NiceStep(top / 3) * 3;
        }

        // Height of a bar or line as a % of the chart.
        public string Pct(decimal value) =>
            (AxisMax == 0 ? 0 : (double)(value / AxisMax * 100)).ToString("0.##", CultureInfo.InvariantCulture) + "%";

        // e.g. "▼ RM 52.00 (12%) vs 1–16 Sep"; "First sales since …" when there is nothing to compare.
        public string Compare(decimal now, decimal before, bool money, out string css)
        {
            css = now > before ? "is-up" : now < before ? "is-down" : "";
            if (before == 0) { css = ""; return now == 0 ? "No sales yet" : $"Nothing to compare in {CompareLabel}"; }
            if (now == before) return $"Same as {CompareLabel}";
            var diff = Math.Abs(now - before);
            var amount = money ? $"RM {diff:N2} ({diff / before * 100:N0}%)" : diff.ToString("0.##");
            return $"{(now > before ? "▲" : "▼")} {amount} vs {CompareLabel}";
        }

        private static decimal NiceStep(decimal x)
        {
            if (x <= 0) return 10;
            var pow = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)x)));
            foreach (var m in new[] { 1m, 2m, 2.5m, 5m, 10m })
                if (m * pow >= x) return m * pow;
            return 10 * pow;
        }

        private static string DayRange(DateTime start, DateTime last) =>
            last.Day == start.Day ? start.Day.ToString() : $"{start.Day}–{last.Day}";

        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    }
}

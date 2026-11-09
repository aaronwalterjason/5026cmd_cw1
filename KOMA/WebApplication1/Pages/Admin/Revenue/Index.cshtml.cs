using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services.Reporting;

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
        [BindProperty(SupportsGet = true)] public string Period { get; set; } = "month";  // day | week | month | year
        [BindProperty(SupportsGet = true)] public DateTime? Date { get; set; }           // for "day" and "week" (any day in the week)
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
        public Dictionary<string, string> Route { get; private set; } = new();   // this period, for the export links

        // ---- Exports ----
        public record ItemSales(string Name, int Qty, decimal Sales);
        public List<ItemSales> TopItems { get; private set; } = new();
        public decimal Discounts { get; private set; }     // promotion savings given to guests

        public record Bucket(string Label, string FullLabel, decimal Amount, int Orders, bool IsCurrent, bool IsFuture);

        public async Task OnGetAsync()
        {
            Now = DateTime.Now;
            var today = DateTime.Today;
            Period = Period?.ToLowerInvariant() switch { "day" => "day", "week" => "week", "year" => "year", _ => "month" };

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
                    Route = new() { ["period"] = "year", ["year"] = y.ToString() };
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
                    Route = new() { ["period"] = "day", ["date"] = d.ToString("yyyy-MM-dd") };
                    PrevRoute = new() { ["period"] = "day", ["date"] = d.AddDays(-1).ToString("yyyy-MM-dd") };
                    NextRoute = new() { ["period"] = "day", ["date"] = d.AddDays(1).ToString("yyyy-MM-dd") };
                    break;

                case "week":   // Monday to Sunday, compared with the week before
                    var w = ReportPeriod.WeekStart(Date ?? today);
                    Date = w;
                    Start = w;
                    End = w.AddDays(7);
                    prevStart = w.AddDays(-7); prevFullEnd = w; nextStart = End;
                    PeriodLabel = "Week of " + ReportPeriod.Span(w, w.AddDays(6));
                    Route = new() { ["period"] = "week", ["date"] = w.ToString("yyyy-MM-dd") };
                    PrevRoute = new() { ["period"] = "week", ["date"] = prevStart.ToString("yyyy-MM-dd") };
                    NextRoute = new() { ["period"] = "week", ["date"] = End.ToString("yyyy-MM-dd") };
                    break;

                default:
                    if (!DateTime.TryParseExact(Month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
                        m = new DateTime(today.Year, today.Month, 1);
                    Start = new DateTime(m.Year, m.Month, 1);
                    End = Start.AddMonths(1);
                    Month = Start.ToString("yyyy-MM");
                    prevStart = Start.AddMonths(-1); prevFullEnd = Start; nextStart = End;
                    PeriodLabel = Start.ToString("MMMM yyyy");
                    Route = new() { ["period"] = "month", ["month"] = Month };
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
                "week" => (ReportPeriod.Span(Start, InProgress ? lastShown : End.AddDays(-1)).ToUpperInvariant(),
                           InProgress ? ReportPeriod.Span(prevStart, lastCompared) : $"last week ({ReportPeriod.Span(prevStart, prevFullEnd.AddDays(-1))})"),
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

            var lines = orders.SelectMany(o => o.Items).ToList();
            TopItems = lines.GroupBy(i => string.IsNullOrWhiteSpace(i.ItemName) ? "(unnamed item)" : i.ItemName)
                .Select(g => new ItemSales(g.Key, g.Sum(i => i.Quantity), g.Sum(i => i.UnitPrice * i.Quantity)))
                .OrderByDescending(x => x.Sales).ThenBy(x => x.Name).ToList();
            Discounts = lines.Where(i => i.ListPrice != null && i.ListPrice > i.UnitPrice).Sum(i => (i.ListPrice!.Value - i.UnitPrice) * i.Quantity);

            var firstPaid = await paid.MinAsync(o => o.PaidAt);
            var firstYear = Math.Min(firstPaid?.Year ?? today.Year, Year ?? today.Year);
            YearOptions = Enumerable.Range(firstYear, today.Year - firstYear + 1).Reverse().ToList();
        }

        // Sales report as PDF, Excel or CSV: ?handler=Export&format=pdf|xlsx|csv (+ the period).
        public async Task<IActionResult> OnGetExportAsync(string format)
        {
            await OnGetAsync();
            var tag = Period switch
            {
                "day" => Start.ToString("yyyy-MM-dd"),
                "week" => "week-" + Start.ToString("yyyy-MM-dd"),
                "year" => Start.ToString("yyyy"),
                _ => Start.ToString("yyyy-MM")
            };
            var doc = new ReportDocument
            {
                Title = "Sales report",
                Period = PeriodLabel + (InProgress ? $" (to {Now:d MMM, h:mm tt})" : ""),
                FileName = $"koma-sales-{tag}",
                GeneratedBy = User.Identity?.Name ?? ""
            };
            doc.Summary.Add(new("Sales", $"RM {TotalRevenue:N2}", Compare(TotalRevenue, PrevRevenue, true, out _)));
            doc.Summary.Add(new("Paid orders", PaidOrders.ToString("N0"), Compare(PaidOrders, PrevOrders, false, out _)));
            doc.Summary.Add(new("Average order", $"RM {AverageOrderValue:N2}", Compare(AverageOrderValue, PrevAverage, true, out _)));
            doc.Summary.Add(new("Items sold", ItemsSold.ToString("N0"), PaidOrders == 0 ? null : $"{ItemsSold / (double)PaidOrders:0.0} per order"));
            if (Discounts > 0) doc.Summary.Add(new("Promotion discounts", $"RM {Discounts:N2}", "given at order time"));

            // 1. Sales over the period (generated sentence + every bucket that has happened)
            var shown = Breakdown.Where(b => !b.IsFuture).ToList();
            var best = shown.Where(b => b.Amount > 0).OrderByDescending(b => b.Amount).FirstOrDefault();
            var change = PrevRevenue == 0 ? "" : TotalRevenue >= PrevRevenue
                ? $", {(TotalRevenue - PrevRevenue) / PrevRevenue * 100:N0}% higher than {CompareLabel}"
                : $", {(PrevRevenue - TotalRevenue) / PrevRevenue * 100:N0}% lower than {CompareLabel}";
            var lead = TotalRevenue == 0
                ? $"No paid orders were recorded for {PeriodLabel}."
                : $"Sales were RM {TotalRevenue:N2} from {PaidOrders:N0} paid order{(PaidOrders == 1 ? "" : "s")}{change}. "
                  + $"The average order was RM {AverageOrderValue:N2}"
                  + (best is null ? "." : $" and the best {BucketWord} was {best.FullLabel} at RM {best.Amount:N2}.");
            var s1 = new ReportSection(BreakdownTitle, lead);
            s1.Tables.Add(new ReportTable(BreakdownTitle,
                new[] { char.ToUpper(BucketWord[0]) + BucketWord[1..], "Orders", "Sales", "Share" },
                shown.Select(b => new[] { b.FullLabel, b.Orders.ToString("N0"), $"RM {b.Amount:N2}", TotalRevenue == 0 ? "—" : $"{b.Amount / TotalRevenue * 100:0}%" }).ToList()));
            doc.Sections.Add(s1);

            // 2. Dishes by sales
            var itemsTotal = TopItems.Sum(i => i.Sales);
            var top = TopItems.FirstOrDefault();
            var s2 = new ReportSection("Sales by dish", top is null ? "No dishes were sold in this period."
                : $"{TopItems.Count:N0} different dishes were sold. {top.Name} brought in the most (RM {top.Sales:N2}, {(itemsTotal == 0 ? 0 : top.Sales / itemsTotal * 100):0}% of dish sales)"
                  + (TopItems.Count > 1 ? $"; {TopItems[^1].Name} brought in the least (RM {TopItems[^1].Sales:N2})." : "."));
            if (TopItems.Count > 0)
                s2.Tables.Add(new ReportTable("Sales by dish", new[] { "Dish", "Sold", "Sales", "Share" },
                    TopItems.Select(i => new[] { i.Name, i.Qty.ToString("N0"), $"RM {i.Sales:N2}", itemsTotal == 0 ? "—" : $"{i.Sales / itemsTotal * 100:0}%" }).ToList()));
            doc.Sections.Add(s2);

            doc.Notes.Add("Only paid orders count, on the date and time they were paid. Unpaid and cancelled orders are left out.");
            doc.Notes.Add("A period that is still running is compared with the same length of the previous period; a single day is compared with the same weekday a week earlier.");
            doc.Notes.Add("Dish sales use the price charged, after any promotion discount. Amounts are in Malaysian ringgit (RM).");

            var file = ReportExport.Build(doc, format);
            return file is null ? BadRequest() : File(file.Value.Bytes, file.Value.ContentType, file.Value.FileName);
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

                case "week":
                    BreakdownTitle = "Sales by day"; BucketWord = "day";
                    Breakdown = Enumerable.Range(0, 7).Select(i =>
                    {
                        var s = Start.AddDays(i);
                        var inDay = orders.Where(o => o.PaidAt!.Value.Date == s).ToList();
                        return new Bucket(s.ToString("ddd d"), s.ToString("ddd d MMM"), inDay.Sum(o => o.TotalAmount), inDay.Count,
                            s.Date == Now.Date, s.Date > Now.Date);
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

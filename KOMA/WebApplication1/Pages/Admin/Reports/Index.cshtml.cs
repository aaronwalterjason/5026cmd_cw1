using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services.Reporting;

namespace KOMA.Pages.Admin.Reports
{
    // Owned by Engineer B. Admin > Reports: one card per stage of the restaurant flow
    //   Book -> Arrive (QR scan seats them) -> Order -> Kitchen -> Serve -> Help -> Team (schedule).
    // Each card: one chart + one table, a CSV of that card (?handler=Csv&part=book) and a link to the page you'd act on.
    // Money stays on Admin > Revenue.
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public const int GuestsPerStaff = 18;   // Team: more guests than this per person on shift = not enough staff

        // ?period=day|week|month&date=yyyy-MM-dd&month=yyyy-MM (default: this month)
        [BindProperty(SupportsGet = true)] public string? Period { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? Date { get; set; }
        [BindProperty(SupportsGet = true)] public string? Month { get; set; }
        public ReportPeriod P { get; private set; } = null!;
        public DateTime From { get; private set; }
        public DateTime To { get; private set; }      // exclusive

        public record Bar(string Label, double Value, string Text, bool Hot = false, string? Full = null);   // Full: label for exports, e.g. "1 pm"
        public record Part(string Key, string Title, string Sub, string Link, string LinkPage, bool Empty)
        {
            public List<Bar> Bars { get; init; } = new();
            public List<string> Head { get; init; } = new();
            public List<string[]> Rows { get; init; } = new();
            public HashSet<(int Row, int Col)> RedCells { get; init; } = new();
        }

        public Part Book { get; private set; } = null!;
        public Part Arrive { get; private set; } = null!;
        public Part Order { get; private set; } = null!;
        public Part Kitchen { get; private set; } = null!;
        public Part Serve { get; private set; } = null!;
        public Part Help { get; private set; } = null!;
        public Part Team { get; private set; } = null!;
        public IEnumerable<Part> Parts => new[] { Book, Arrive, Order, Kitchen, Serve, Help, Team };

        // extras for some charts
        public int StaffShare { get; private set; }          // Serve: % served by staff
        public List<double> TeamStaff { get; private set; } = new();   // Team: staff on shift per hour (avg), same order as Team.Bars

        // For the exported report: key figures and one generated sentence per stage.
        public List<ReportFigure> Figures { get; } = new();
        public Dictionary<string, string> Leads { get; } = new();

        public async Task OnGetAsync() => await BuildAsync();

        public async Task<IActionResult> OnGetCsvAsync(string part)
        {
            await BuildAsync();
            var p = Parts.FirstOrDefault(x => x.Key == part);
            if (p is null) return NotFound();
            var sb = new StringBuilder();
            static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
            sb.AppendLine(string.Join(",", p.Head.Select(Q)));
            foreach (var r in p.Rows) sb.AppendLine(string.Join(",", r.Select(Q)));
            sb.AppendLine();
            sb.AppendLine(string.Join(",", new[] { "Chart", "Value" }.Select(Q)));
            foreach (var b in p.Bars) sb.AppendLine(Q(b.Label) + "," + Q(b.Text));
            var name = $"koma-{p.Key}-{P.FileTag}.csv";
            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", name);
        }

        // Whole report as PDF, Excel or CSV: ?handler=Export&format=pdf|xlsx|csv (+ the period).
        public async Task<IActionResult> OnGetExportAsync(string format)
        {
            await BuildAsync();
            var doc = new ReportDocument
            {
                Title = "Operations report",
                Period = P.LongLabel,
                FileName = $"koma-operations-{P.FileTag}",
                GeneratedBy = User.Identity?.Name ?? ""
            };
            doc.Summary.AddRange(Figures);

            var titles = new Dictionary<string, (string Section, string Chart, string[] ChartHead, string Table)>
            {
                ["book"] = ("Bookings", "Booking funnel", new[] { "Stage", "Bookings" }, "Show-up rate"),
                ["arrive"] = ("Arrivals", "Arrival against booked time", new[] { "Arrival", "Bookings" }, "No-shows by weekday"),
                ["order"] = ("Dishes ordered", "Most ordered dishes", new[] { "Dish", "Ordered" }, "Least ordered dishes"),
                ["kitchen"] = ("Kitchen", "Order to ready, by hour ordered", new[] { "Hour", "Average" }, "Slowest dishes"),
                ["serve"] = ("Service", "Ready to table, by day", new[] { "Day", "Average" }, "Served by"),
                ["help"] = ("Help requests", "Requests by type", new[] { "Type", "Requests" }, "Time to answer"),
                ["team"] = ("Staffing", "Guests and staff on shift, by hour (daily average)", new[] { "Hour", "Guests", "Staff", "Enough staff" }, "Busiest hours per staff member"),
            };
            foreach (var p in Parts)
            {
                var tt = titles[p.Key];
                var section = new ReportSection(tt.Section, p.Empty ? "No activity in this period." : Leads.GetValueOrDefault(p.Key));
                if (!p.Empty)
                {
                    List<string[]> chart = p.Key == "team"
                        ? p.Bars.Select((b, i) => new[] { b.Full ?? b.Label, b.Value.ToString("0.#"), TeamStaff[i].ToString("0.#"), b.Hot ? "No" : "Yes" }).ToList()
                        : p.Bars.Select(b => new[] { b.Full ?? b.Label, b.Text }).ToList();
                    if (chart.Count > 0) section.Tables.Add(new ReportTable(tt.Chart, tt.ChartHead, chart));
                    if (p.Rows.Count > 0) section.Tables.Add(new ReportTable(tt.Table, p.Head, p.Rows));
                }
                doc.Sections.Add(section);
            }
            doc.Notes.Add("Cancelled bookings and cancelled orders are left out.");
            doc.Notes.Add("Arrival is the time the table's QR code was scanned. A booking is a no-show when nobody was seated or ordered 30 minutes after the booked time.");
            doc.Notes.Add("Kitchen time runs from the order to the dish being marked ready; service time from ready to served.");
            doc.Notes.Add($"Staffing uses the published duty schedule. An hour is short-staffed when there are more than {GuestsPerStaff} guests per staff member on shift.");
            doc.Notes.Add("Sales and money are in the separate Sales report (Admin > Revenue).");

            var file = ReportExport.Build(doc, format);
            return file is null ? BadRequest() : File(file.Value.Bytes, file.Value.ContentType, file.Value.FileName);
        }

        private static string Min(double m) => m < 1 ? "<1 min" : $"{Math.Round(m)} min";
        private static string Hour(int h) => h == 0 ? "12 am" : h < 12 ? $"{h} am" : h == 12 ? "12 pm" : $"{h - 12} pm";
        private static string HourShort(int h) => (h % 12 == 0 ? 12 : h % 12).ToString();

        private async Task BuildAsync()
        {
            var now = DateTime.Now;
            P = new ReportPeriod(Period, Date, Month, now);
            From = P.Start;
            To = P.End;

            var res = await _db.Reservations.AsNoTracking()
                .Where(r => r.StartTime >= From && r.StartTime < To && r.Status != ReservationStatus.Cancelled)
                .Select(r => new { r.Id, r.StartTime }).ToListAsync();
            var sessions = await _db.TableServiceSessions.AsNoTracking()
                .Where(s => s.StartTime >= From && s.StartTime < To)
                .Select(s => new { s.ReservationId, s.StartTime, s.GuestCount }).ToListAsync();
            var orders = await _db.Orders.AsNoTracking()
                .Where(o => o.OrderedAt >= From && o.OrderedAt < To && o.Status != OrderStatus.Cancelled)
                .Select(o => new { o.Id, o.ReservationId, o.OrderedAt, Paid = o.PaymentStatus == PaymentStatus.Paid }).ToListAsync();
            var items = await _db.OrderItems.AsNoTracking()
                .Where(i => i.Order!.OrderedAt >= From && i.Order.OrderedAt < To && i.Order.Status != OrderStatus.Cancelled)
                .Select(i => new { Name = i.ItemName != "" ? i.ItemName : i.MenuItem!.Name, i.Quantity, i.Order!.OrderedAt, i.ReadyAt, i.ServedAt, i.ServedVia }).ToListAsync();
            var help = await _db.HelpRequests.AsNoTracking()
                .Where(h => h.CreatedAt >= From && h.CreatedAt < To)
                .Select(h => new { h.Type, h.CreatedAt, h.TakenAt, h.DoneAt }).ToListAsync();

            // ---------- Book: booked -> arrived -> ordered -> paid ----------
            var seatedIds = sessions.Where(s => s.ReservationId != null).Select(s => s.ReservationId!.Value).ToHashSet();
            var orderedIds = orders.Where(o => o.ReservationId != null).Select(o => o.ReservationId!.Value).ToHashSet();
            var paidIds = orders.Where(o => o.ReservationId != null && o.Paid).Select(o => o.ReservationId!.Value).ToHashSet();
            var booked = res.Count;
            var arrived = res.Count(r => seatedIds.Contains(r.Id) || orderedIds.Contains(r.Id));
            var ordered = res.Count(r => orderedIds.Contains(r.Id));
            var paid = res.Count(r => paidIds.Contains(r.Id));
            var walkIns = sessions.Count(s => s.ReservationId == null);
            var due = res.Count(r => r.StartTime < now);
            var showed = res.Count(r => r.StartTime < now && (seatedIds.Contains(r.Id) || orderedIds.Contains(r.Id)));
            Book = new Part("book", "Book", $"{booked:N0} bookings", "Bookings", "/Staff/Reservation/Index", booked + walkIns == 0)
            {
                Bars = new() { new("Booked", booked, booked.ToString("N0")), new("Arrived", arrived, arrived.ToString("N0")), new("Ordered", ordered, ordered.ToString("N0")), new("Paid", paid, paid.ToString("N0")) },
                Head = new() { "Type", "Visits", "Showed up" },
                Rows = new() { new[] { "Booked", showed.ToString("N0"), due == 0 ? "—" : $"{showed * 100 / due}%" }, new[] { "Walk-in", walkIns.ToString("N0"), "—" } }
            };
            Figures.Add(new("Bookings", booked.ToString("N0"), $"{walkIns:N0} walk-ins"));
            Figures.Add(new("Show-up rate", due == 0 ? "—" : $"{showed * 100 / due}%", due == 0 ? "no bookings due yet" : $"{showed:N0} of {due:N0} due"));
            Leads["book"] = booked == 0
                ? $"There were no bookings in this period{(walkIns > 0 ? $"; {walkIns:N0} tables were walk-ins" : "")}."
                : $"{booked:N0} bookings were made for this period. {arrived:N0} arrived, {ordered:N0} ordered and {paid:N0} paid"
                  + (walkIns > 0 ? $"; there were also {walkIns:N0} walk-in tables." : ".");

            // ---------- Arrive: QR scan (table seated) vs booked time ----------
            var startOf = res.ToDictionary(r => r.Id, r => r.StartTime);
            var late = sessions.Where(s => s.ReservationId != null && startOf.ContainsKey(s.ReservationId.Value))
                .Select(s => (s.StartTime - startOf[s.ReservationId!.Value]).TotalMinutes).ToList();
            var noShows = res.Where(r => r.StartTime < now.AddMinutes(-30) && !seatedIds.Contains(r.Id) && !orderedIds.Contains(r.Id)).ToList();
            Arrive = new Part("arrive", "Arrive", late.Count == 0 && noShows.Count == 0 ? "" : $"{noShows.Count} no-shows", "Floors", "/Admin/Layouts/Index", late.Count == 0 && noShows.Count == 0)
            {
                Bars = new()
                {
                    new("Early", late.Count(m => m < -5), late.Count(m => m < -5).ToString()),
                    new("On time", late.Count(m => m >= -5 && m <= 5), late.Count(m => m >= -5 && m <= 5).ToString()),
                    new("5–15 late", late.Count(m => m > 5 && m <= 15), late.Count(m => m > 5 && m <= 15).ToString()),
                    new("15+ late", late.Count(m => m > 15), late.Count(m => m > 15).ToString()),
                    new("No-show", noShows.Count, noShows.Count.ToString(), true)
                },
                Head = new() { "Day", "No-shows" },
                Rows = noShows.GroupBy(r => r.StartTime.DayOfWeek).OrderByDescending(g => g.Count()).Take(4)
                    .Select(g => new[] { g.Key.ToString()[..3], g.Count().ToString() }).ToList()
            };
            var onTime = late.Count(m => m >= -5 && m <= 5);
            Figures.Add(new("No-shows", noShows.Count.ToString("N0"), late.Count == 0 ? null : $"{onTime * 100 / late.Count}% arrived on time"));
            Leads["arrive"] = late.Count == 0
                ? $"No booked tables were seated in this period. {noShows.Count:N0} bookings were no-shows."
                : $"{onTime * 100 / late.Count}% of seated bookings arrived within 5 minutes of the booked time and {late.Count(m => m > 15):N0} arrived more than 15 minutes late. "
                  + (noShows.Count == 0 ? "There were no no-shows." : $"{noShows.Count:N0} bookings were no-shows.");

            // ---------- Order: most / least ordered dishes ----------
            var dishes = KOMA.Services.DishPopularity.Rank(items.Select(i => ((string?)i.Name, i.Quantity)));
            Order = new Part("order", "Order", $"{dishes.Sum(d => d.Qty):N0} dishes", "Menu", "/Admin/MenuItems/Index", dishes.Count == 0)
            {
                Bars = dishes.Take(6).Select(d => new Bar(d.Name, d.Qty, d.Qty.ToString("N0"))).ToList(),
                Head = new() { "Least ordered", "Dishes" },
                Rows = dishes.Count <= 6 ? new() : dishes.TakeLast(3).OrderBy(d => d.Qty).Select(d => new[] { d.Name, d.Qty.ToString("N0") }).ToList()
            };
            Figures.Add(new("Dishes ordered", dishes.Sum(d => d.Qty).ToString("N0"), dishes.Count == 0 ? null : $"top: {dishes[0].Name}"));
            if (dishes.Count > 0)
                Leads["order"] = $"{dishes.Sum(d => d.Qty):N0} dishes were ordered across {dishes.Count:N0} menu items. "
                    + $"{dishes[0].Name} was ordered most ({dishes[0].Qty:N0})"
                    + (dishes.Count > 6 ? $" and {dishes[^1].Name} least ({dishes[^1].Qty:N0})." : ".");

            // ---------- Kitchen: order -> ready ----------
            var cooked = items.Where(i => i.ReadyAt != null && i.ReadyAt >= i.OrderedAt).Select(i => new { i.Name, i.OrderedAt.Hour, M = (i.ReadyAt!.Value - i.OrderedAt).TotalMinutes }).ToList();
            var kHours = cooked.GroupBy(c => c.Hour).OrderBy(g => g.Key).ToList();
            Kitchen = new Part("kitchen", "Kitchen", cooked.Count == 0 ? "" : $"{Min(cooked.Average(c => c.M))} to ready", "Orders", "/Admin/Orders/Index", cooked.Count == 0)
            {
                Bars = kHours.Select(g => new Bar(HourShort(g.Key), g.Average(c => c.M), Min(g.Average(c => c.M)), Full: Hour(g.Key))).ToList(),
                Head = new() { "Dish", "To ready" },
                Rows = cooked.GroupBy(c => c.Name).Where(g => g.Count() >= 1).OrderByDescending(g => g.Average(c => c.M)).Take(4)
                    .Select(g => new[] { g.Key ?? "", Min(g.Average(c => c.M)) }).ToList()
            };
            Figures.Add(new("Order to ready", cooked.Count == 0 ? "—" : Min(cooked.Average(c => c.M)), "kitchen average"));
            if (cooked.Count > 0)
            {
                var slowHour = kHours.OrderByDescending(g => g.Average(c => c.M)).First();
                Leads["kitchen"] = $"Dishes took {Min(cooked.Average(c => c.M))} on average from order to ready. "
                    + $"The slowest hour was {Hour(slowHour.Key)} at {Min(slowHour.Average(c => c.M))}.";
            }

            // ---------- Serve: staff vs tablet, ready -> served ----------
            var served = items.Where(i => i.ServedAt != null).ToList();
            var byStaff = served.Where(i => i.ServedVia != "Tablet").Sum(i => i.Quantity);
            var byTablet = served.Where(i => i.ServedVia == "Tablet").Sum(i => i.Quantity);
            StaffShare = byStaff + byTablet == 0 ? 0 : (int)Math.Round(byStaff * 100.0 / (byStaff + byTablet));
            var waits = served.Where(i => i.ReadyAt != null && i.ServedAt >= i.ReadyAt).Select(i => new { i.ServedAt, M = (i.ServedAt!.Value - i.ReadyAt!.Value).TotalMinutes }).ToList();
            Serve = new Part("serve", "Serve", waits.Count == 0 ? "" : $"{Min(waits.Average(w => w.M))} ready → table", "Orders", "/Admin/Orders/Index", served.Count == 0)
            {
                Bars = waits.GroupBy(w => w.ServedAt!.Value.Date).OrderBy(g => g.Key).Select(g => new Bar(g.Key.ToString("d MMM"), g.Average(w => w.M), Min(g.Average(w => w.M)))).ToList(),
                Head = new() { "Served", "Dishes" },
                Rows = new() { new[] { "By staff", byStaff.ToString("N0") }, new[] { "On the tablet", byTablet.ToString("N0") } }
            };
            Figures.Add(new("Ready to table", waits.Count == 0 ? "—" : Min(waits.Average(w => w.M)), $"{StaffShare}% served by staff"));
            if (served.Count > 0)
                Leads["serve"] = (waits.Count == 0 ? "" : $"Ready dishes reached the table in {Min(waits.Average(w => w.M))} on average. ")
                    + $"{StaffShare}% of dishes were served by staff and {100 - StaffShare}% were confirmed on the table tablet.";

            // ---------- Help: requests by type, time to answer ----------
            var types = Enum.GetValues<HelpType>();
            Help = new Part("help", "Help", help.Count == 0 ? "" : $"{help.Count:N0} requests", "Orders", "/Admin/Orders/Index", help.Count == 0)
            {
                Bars = types.Select(t => new Bar(t.DisplayName(), help.Count(h => h.Type == t), help.Count(h => h.Type == t).ToString())).ToList(),
                Head = new() { "Type", "To answer" },
                Rows = types.Select(t => (t, list: help.Where(h => h.Type == t && (h.TakenAt ?? h.DoneAt) != null).Select(h => ((h.TakenAt ?? h.DoneAt)!.Value - h.CreatedAt).TotalMinutes).ToList()))
                    .Where(x => x.list.Count > 0).Select(x => new[] { x.t.DisplayName(), Min(x.list.Average()) }).ToList()
            };
            var answered = help.Where(h => (h.TakenAt ?? h.DoneAt) != null).Select(h => ((h.TakenAt ?? h.DoneAt)!.Value - h.CreatedAt).TotalMinutes).ToList();
            Figures.Add(new("Help requests", help.Count.ToString("N0"), answered.Count == 0 ? null : $"{Min(answered.Average())} to answer"));
            if (help.Count > 0)
            {
                var topType = types.OrderByDescending(x => help.Count(h => h.Type == x)).First();
                Leads["help"] = $"Guests sent {help.Count:N0} help requests, most often {topType.DisplayName().ToLowerInvariant()}. "
                    + (answered.Count == 0 ? "None were answered yet." : $"Staff answered in {Min(answered.Average())} on average; {help.Count - answered.Count:N0} were not answered.");
            }

            // ---------- Team: guests per hour vs staff on shift (schedule) ----------
            var fromD = DateOnly.FromDateTime(From); var toD = DateOnly.FromDateTime(To);
            List<(DateOnly Date, TimeOnly Starts, TimeOnly Ends)> shifts = new();
            try
            {
                shifts = (await _db.ShiftAssignments.AsNoTracking()
                    .Where(a => a.Date >= fromD && a.Date < toD && !a.IsLeave && a.ShiftId != null)
                    .Select(a => new { a.Date, a.Shift!.Starts, a.Shift.Ends }).ToListAsync())
                    .Select(a => (a.Date, a.Starts, a.Ends)).ToList();
            }
            catch { /* schedule tables not created yet */ }
            bool OnAt(TimeOnly s, TimeOnly e, int h) { var t = new TimeOnly(h, 30); return s < e ? t >= s && t < e : t >= s || t < e; }
            var openDays = sessions.Select(s => s.StartTime.Date).Distinct().Count();
            var schedDays = shifts.Select(s => s.Date).Distinct().Count();
            var hours = Enumerable.Range(10, 13).Select(h => h % 24).ToList();
            var teamRows = new List<(DayOfWeek Day, int Hour, double Guests, double Staff)>();
            foreach (var g in sessions.GroupBy(s => (s.StartTime.DayOfWeek, s.StartTime.Hour)))
            {
                var days = sessions.Where(s => s.StartTime.DayOfWeek == g.Key.DayOfWeek).Select(s => s.StartTime.Date).Distinct().Count();
                var guests = g.Sum(s => s.GuestCount) / (double)Math.Max(1, days);
                var sDays = shifts.Where(s => s.Date.DayOfWeek == g.Key.DayOfWeek).Select(s => s.Date).Distinct().ToList();
                var staff = sDays.Count == 0 ? 0 : shifts.Count(s => s.Date.DayOfWeek == g.Key.DayOfWeek && OnAt(s.Starts, s.Ends, g.Key.Hour)) / (double)sDays.Count;
                teamRows.Add((g.Key.DayOfWeek, g.Key.Hour, guests, staff));
            }
            var team = new Part("team", "Team", "", "Schedule", "/Admin/Accounts/Index", sessions.Count == 0);
            foreach (var h in hours)
            {
                var guests = openDays == 0 ? 0 : sessions.Where(s => s.StartTime.Hour == h).Sum(s => s.GuestCount) / (double)openDays;
                var staff = schedDays == 0 ? 0 : shifts.Count(s => OnAt(s.Starts, s.Ends, h)) / (double)schedDays;
                team.Bars.Add(new Bar(HourShort(h), guests, $"{guests:0.#} guests · {staff:0.#} staff", schedDays > 0 && guests > staff * GuestsPerStaff, Hour(h)));
                TeamStaff.Add(staff);
            }
            var worst = teamRows.Where(r => r.Guests > 0).OrderByDescending(r => r.Staff == 0 ? double.MaxValue : r.Guests / r.Staff).Take(4).ToList();
            team.Head.AddRange(new[] { "Day", "Hour", "Staff", "Guests / staff" });
            for (var i = 0; i < worst.Count; i++)
            {
                var r = worst[i];
                var ratio = r.Staff == 0 ? double.PositiveInfinity : r.Guests / r.Staff;
                team.Rows.Add(new[] { r.Day.ToString()[..3], Hour(r.Hour), r.Staff.ToString("0.#"), double.IsInfinity(ratio) ? "no staff" : ratio.ToString("0") });
                if (ratio > GuestsPerStaff) team.RedCells.Add((i, 3));
            }
            var shortHours = team.Bars.Count(b => b.Hot);
            Team = team with { Sub = schedDays == 0 ? "no schedule yet" : shortHours == 0 ? "enough staff" : $"{shortHours} short hours" };
            Figures.Add(new("Short-staffed hours", schedDays == 0 ? "—" : shortHours.ToString(), schedDays == 0 ? "no schedule for this period" : "of 13 opening hours"));
            if (sessions.Count > 0)
                Leads["team"] = schedDays == 0
                    ? "No duty schedule was published for this period, so staffing could not be compared with guest numbers."
                    : shortHours == 0
                        ? $"Every opening hour had enough staff on shift (at most {GuestsPerStaff} guests per staff member)."
                        : $"{shortHours} opening hour{(shortHours == 1 ? " was" : "s were")} short-staffed: "
                          + string.Join(", ", team.Bars.Where(b => b.Hot).Select(b => b.Full)) + ".";
        }
    }
}

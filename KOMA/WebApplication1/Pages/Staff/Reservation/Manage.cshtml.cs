using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Staff.Reservations;

[Authorize(Roles = "Staff,Administrator")]
public class ManageModel(ApplicationDbContext context) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public ReservationStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public const int PageSize = 20;

    public List<KOMA.Models.Reservation> Bookings { get; private set; } = [];

    public int Total { get; private set; }
    public int Pending { get; private set; }
    public int Confirmed { get; private set; }
    public int Cancelled { get; private set; }

    public int TotalPages =>
        Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    public DateTime Now { get; } = ReservationRules.MalaysiaNow;

    public async Task OnGetAsync()
    {
        // Default to today onward. An explicitly blank date allows history.
        if (!Request.Query.ContainsKey(nameof(From)))
        {
            From = DateOnly.FromDateTime(Now);
        }

        if (From == DateOnly.MaxValue || To == DateOnly.MaxValue)
        {
            ModelState.AddModelError(
                "",
                "Choose a date before 31 December 9999.");
        }

        if (From.HasValue && To.HasValue && From > To)
        {
            ModelState.AddModelError(
                "",
                "The end date must be on or after the start date.");
        }

        if (Status.HasValue && !Enum.IsDefined(Status.Value))
        {
            ModelState.AddModelError(
                nameof(Status),
                "Choose a valid reservation status.");
        }

        if (!ModelState.IsValid)
        {
            return;
        }

        var query = context.Reservations.AsNoTracking();

        if (From.HasValue)
        {
            var start = From.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(r => r.StartTime >= start);
        }

        if (To.HasValue)
        {
            var end = To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(r => r.StartTime < end);
        }

        Search = Search?.Trim();

        if (!string.IsNullOrEmpty(Search))
        {
            var term = Search;

            var reference = term.StartsWith(
                "R-",
                StringComparison.OrdinalIgnoreCase)
                    ? term[2..]
                    : term;

            var id = int.TryParse(reference, out var parsed)
                ? parsed
                : 0;

            query = query.Where(r =>
                r.Id == id ||
                r.CustomerName.Contains(term) ||
                r.ContactNumber.Contains(term) ||
                (r.Email != null && r.Email.Contains(term)) ||
                r.RestaurantTable.TableNumber.Contains(term));
        }

        // Summary counts respect date/search, across all statuses.
        var counts = await query
            .GroupBy(r => r.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        Pending = counts
            .FirstOrDefault(c => c.Status == ReservationStatus.Pending)
            ?.Count ?? 0;

        Confirmed = counts
            .FirstOrDefault(c => c.Status == ReservationStatus.Confirmed)
            ?.Count ?? 0;

        Cancelled = counts
            .FirstOrDefault(c => c.Status == ReservationStatus.Cancelled)
            ?.Count ?? 0;

        if (Status.HasValue)
        {
            query = query.Where(r => r.Status == Status.Value);
        }

        Total = await query.CountAsync();
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);

        Bookings = await query
            .Include(r => r.RestaurantTable)
            .ThenInclude(t => t.DiningArea)
            .OrderBy(r => r.StartTime)
            .ThenBy(r => r.Id)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }
}
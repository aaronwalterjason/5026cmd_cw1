using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Booking = KOMA.Models.Reservation;

namespace KOMA.Pages.Staff.Reservations;

[Authorize(Roles = "Staff,Administrator")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TablePanelModel(ApplicationDbContext db, DiningOrderService dining) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int TableId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? BookingId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SessionId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Date { get; set; }

    [BindProperty(SupportsGet = true)]
    public TimeOnly? Time { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? At { get; set; }

    public TableSelection Selection { get; private set; } = null!;
    public RestaurantTable Table { get; private set; } = null!;

    public List<Booking> Bookings { get; private set; } = [];
    public List<TableServiceSession> WalkIns { get; private set; } = [];

    public Booking? Booking { get; private set; }
    public TableServiceSession? Session { get; private set; }

    public List<Order> Orders { get; private set; } = [];
    public IEnumerable<Order> Outstanding => Orders.Where(o => o.Status != OrderStatus.Cancelled && o.PaymentStatus == PaymentStatus.Unpaid);
    public List<RestaurantTable> MoveTargets { get; private set; } = [];
    private IQueryable<RestaurantTable> AvailableFor(TableServiceSession visit)
    {
        var now = ReservationRules.MalaysiaNow;
        return db.RestaurantTables.Where(t => t.Id != visit.RestaurantTableId && t.IsActive &&
            t.DiningArea!.IsActive && t.Capacity >= visit.GuestCount &&
            !db.Reservations.Any(r => r.RestaurantTableId == t.Id && r.Status != ReservationStatus.Cancelled &&
                r.StartTime < visit.EndTime && r.EndTime > now) &&
            !db.TableServiceSessions.Any(s => s.RestaurantTableId == t.Id && s.StartTime < visit.EndTime && s.EndTime > now));
    }

    public async Task<IActionResult> OnPostMoveWalkInAsync(int destination)
    {
        if (!ModelState.IsValid || !SessionId.HasValue) return BadRequest("Choose a walk-in visit.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await dining.LockAsync();
        var now = ReservationRules.MalaysiaNow;
        var visit = await db.TableServiceSessions.SingleOrDefaultAsync(s => s.Id == SessionId &&
            s.RestaurantTableId == TableId && s.ReservationId == null && s.StartTime <= now && s.EndTime > now);
        if (visit == null || !await AvailableFor(visit).AnyAsync(t => t.Id == destination))
            return new ConflictObjectResult("The visit or table availability changed. Reopen the table to try again.");
        visit.RestaurantTableId = destination;
        await db.Orders.Where(o => o.TableServiceSessionId == visit.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.RestaurantTableId, (int?)destination));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new OkResult();
    }

    public string BillToken => string.Join(".", Outstanding.Select(OrderWorkflowService.ReviewToken));
    private async Task<bool> LoadAsync(bool trackOrders = false)
    {
        var settings = await db.RestaurantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync();

        var selection = TableSelection.Resolve(
            settings, Date, Time, At);

        var table = await db.RestaurantTables
            .AsNoTracking()
            .Include(t => t.DiningArea)
            .SingleOrDefaultAsync(t => t.Id == TableId);

        if (selection == null || table == null)
            return false;

        Selection = selection;
        Table = table;

        var start = selection.Start;
        var end = selection.End;

        Bookings = await db.Reservations
            .AsNoTracking()
            .Where(r =>
                r.RestaurantTableId == TableId &&
                r.Status != ReservationStatus.Cancelled &&
                (selection.IsNow
                    ? r.StartTime <= start && r.EndTime > start
                    : r.StartTime < end && r.EndTime > start))
            .OrderBy(r => r.StartTime)
            .ToListAsync();

        WalkIns = await db.TableServiceSessions
            .AsNoTracking()
            .Where(s =>
                s.RestaurantTableId == TableId &&
                s.ReservationId == null &&
                (selection.IsNow
                    ? s.StartTime <= start && s.EndTime > start
                    : s.StartTime < end && s.EndTime > start))
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        if (BookingId.HasValue && SessionId.HasValue)
            return false;

        if (BookingId.HasValue)
        {
            Booking = Bookings.FirstOrDefault(
                b => b.Id == BookingId.Value);

            if (Booking == null)
                return false;
        }
        else if (SessionId.HasValue)
        {
            Session = WalkIns.FirstOrDefault(
                s => s.Id == SessionId.Value);

            if (Session == null)
                return false;
        }
        else if (Bookings.Count > 0)
        {
            Booking = Bookings[0];
            BookingId = Booking.Id;
        }
        else if (WalkIns.Count > 0)
        {
            Session = WalkIns[0];
            SessionId = Session.Id;
        }

        if (Booking != null)
        {
            Session = await db.TableServiceSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(s =>
                    s.ReservationId == Booking.Id &&
                    s.RestaurantTableId == TableId);
        }

        var selectedBookingId = Booking?.Id;
        var selectedSessionId = Session?.Id;

        if (selectedBookingId.HasValue || selectedSessionId.HasValue)
        {
            var query = trackOrders ? db.Orders.AsTracking() : db.Orders.AsNoTracking();
            Orders = await query
                .Include(o => o.Items)
                .ThenInclude(i => i.MenuItem)
                .Where(o => o.RestaurantTableId == TableId && o.Status != OrderStatus.AwaitingArrival && o.Status != OrderStatus.Pending)
                .Where(o =>
                    // Customer pre-orders linked directly to this reservation.
                    (selectedBookingId.HasValue &&
                     o.ReservationId == selectedBookingId.Value &&
                     (o.TableServiceSessionId == null ||
                      (selectedSessionId.HasValue &&
                       o.TableServiceSessionId == selectedSessionId.Value)))
                    ||
                    // Walk-in orders and older visit-linked orders.
                    (selectedSessionId.HasValue &&
                     o.TableServiceSessionId == selectedSessionId.Value &&
                     o.ReservationId == null))
                .OrderBy(o => o.Id)
                .ToListAsync();


        }

        return true;
    }

    public async Task<IActionResult> OnPostSettleAsync(PaymentMethod? method, string? billToken)
    {
        if (!ModelState.IsValid || method is not (PaymentMethod.Cash or PaymentMethod.Card or PaymentMethod.TouchNGo) ||
            (!BookingId.HasValue && !SessionId.HasValue)) return BadRequest("Choose a visit and payment method.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        if (!await LoadAsync(true) || !Outstanding.Any() || BillToken != billToken)
            return ConflictResultForBill();
        foreach (var order in Outstanding.ToList()) OrderWorkflowService.RecordPayment(order, method.Value);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new OkResult();
    }

    private static IActionResult ConflictResultForBill() =>
        new ConflictObjectResult("The bill changed. Reopen Food orders and review it before settling.");

    public async Task<IActionResult> OnGetAsync()
    {
        if (!ModelState.IsValid || !await LoadAsync())
        {
            return BadRequest(
                "Selection changed or is invalid. Close and reopen the table.");
        }

        if (Booking == null && Session != null && Session.EndTime > ReservationRules.MalaysiaNow)
            MoveTargets = await AvailableFor(Session).OrderBy(t => t.TableNumber).ToListAsync();
        return Partial("_TablePanel", this);
    }
}

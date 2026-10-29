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
public class TablePanelModel(
    ApplicationDbContext db,
    ReservationService reservations,
    TableSessionService sessions,
    OrderWorkflowService workflow) : PageModel
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
    public List<Order> Unlinked { get; private set; } = [];

    public bool CanEdit =>
        Booking != null &&
        Booking.Status != ReservationStatus.Cancelled &&
        Booking.StartTime > ReservationRules.MalaysiaNow;

    public bool CanCreate =>
        Booking == null &&
        Session == null &&
        Bookings.Count == 0 &&
        WalkIns.Count == 0 &&
        Selection.CanCreate &&
        Table.IsActive &&
        Table.DiningArea?.IsActive == true;

    public object Route => new
    {
        TableId,
        BookingId,
        SessionId,
        Date = Date?.ToString("yyyy-MM-dd"),
        Time = Time?.ToString("HH:mm"),
        At = At?.ToString("s")
    };

    private async Task<bool> LoadAsync()
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
            Orders = await db.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .ThenInclude(i => i.MenuItem)
                .Where(o => o.RestaurantTableId == TableId)
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

            // Only genuinely unassigned orders belong in this section.
            // Pre-orders already linked to a reservation must not appear here.
            Unlinked = await db.Orders
                .AsNoTracking()
                .Where(o =>
                    o.RestaurantTableId == TableId &&
                    o.ReservationId == null &&
                    o.TableServiceSessionId == null &&
                    o.Status != OrderStatus.Cancelled &&
                    o.Status != OrderStatus.Completed)
                .OrderByDescending(o => o.OrderedAt)
                .Take(100)
                .ToListAsync();
        }

        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!ModelState.IsValid || !await LoadAsync())
        {
            return BadRequest(
                "Selection changed or is invalid. Close and reopen the table.");
        }

        return Partial("_TablePanel", this);
    }

    public async Task<IActionResult> OnPostSaveAsync(
        ReservationInput input)
    {
        if (!await LoadAsync())
            return Error("The selected booking changed. Reopen this table.");

        if (Booking != null ? !CanEdit : !CanCreate)
            return Error("Reservations cannot be saved for this selection.");

        // Take the table and time from the verified selection.
        input.RestaurantTableId = TableId;
        input.Date = DateOnly.FromDateTime(
            Booking?.StartTime ?? Selection.Start);
        input.Time = TimeOnly.FromDateTime(
            Booking?.StartTime ?? Selection.Start);

        ModelState.Clear();

        if (!TryValidateModel(input))
        {
            var message = string.Join(" ",
                ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));

            return Error(message);
        }

        return await RunAsync(async () =>
            (await reservations.SaveAsync(input, Booking?.Id, User)).Error);
    }

    public async Task<IActionResult> OnPostBookingStatusAsync(
        string rowVersion,
        bool confirm,
        bool confirmCancel)
    {
        if (!ModelState.IsValid ||
            !await LoadAsync() ||
            Booking == null)
        {
            return Error("Refresh this booking first.");
        }

        if (!confirm && !confirmCancel)
            return Error("Tick the cancellation confirmation first.");

        return await RunAsync(() =>
            reservations.ChangeStatusAsync(
                Booking.Id, rowVersion, confirm, User));
    }

    public async Task<IActionResult> OnPostOrderAsync(
        int orderId,
        OrderStatus? expectedStatus,
        string? reviewToken,
        bool arrivalConfirmed)
    {
        if (!ModelState.IsValid ||
            !await LoadAsync() ||
            (Booking == null && Session == null) ||
            expectedStatus is not
                (OrderStatus.Pending or
                 OrderStatus.AwaitingArrival or
                 OrderStatus.ReadyToServe) ||
            !Orders.Any(o => o.Id == orderId) ||
            string.IsNullOrWhiteSpace(reviewToken))
        {
            return Error(
                "The order does not match this booking or visit. " +
                "Close and reopen the table.");
        }

        return await RunAsync(() =>
            workflow.AdvanceAsync(
                id: orderId,
                expected: expectedStatus.Value,
                expectedSessionId: Booking == null ? Session?.Id : null,
                expectedBookingId: Booking?.Id,
                expectedTableId: TableId,
                expectedReview: reviewToken,
                arrivalConfirmed: arrivalConfirmed));
    }

    public async Task<IActionResult> OnPostLinkAsync(
        int orderId,
        bool reviewed)
    {
        if (!ModelState.IsValid ||
            !reviewed ||
            !await LoadAsync() ||
            (Booking == null && Session == null) ||
            !Unlinked.Any(o => o.Id == orderId))
        {
            return Error(
                "Select an unlinked order and confirm its customer " +
                "matches this visit.");
        }

        return await RunAsync(() =>
            sessions.LinkOrderAsync(
                TableId,
                Booking?.Id,
                Booking == null ? Session?.Id : null,
                orderId));
    }

    public async Task<IActionResult> OnPostWalkInAsync(
        string name,
        string? phone,
        int guests,
        int minutes)
    {
        if (!ModelState.IsValid ||
            !await LoadAsync() ||
            !Selection.IsNow ||
            Bookings.Count > 0 ||
            WalkIns.Count > 0)
        {
            return Error(
                "Use Now on an available table to record a walk-in.");
        }

        return await RunAsync(async () =>
            (await sessions.CreateWalkInAsync(
                TableId, name, phone, guests, minutes)).Error);
    }

    private JsonResult Error(string message) =>
        new(new { ok = false, message });

    private async Task<IActionResult> RunAsync(
        Func<Task<string?>> action)
    {
        try
        {
            var error = await action();

            return error == null
                ? new JsonResult(new
                {
                    ok = true,
                    message = "Saved. Table information refreshed."
                })
                : Error(error);
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            return Error(
                "The database is busy or unavailable. " +
                "Refresh before trying again.");
        }
        catch (DbUpdateException)
        {
            return Error(
                "The information changed or could not be saved. " +
                "Refresh before trying again.");
        }
    }
}
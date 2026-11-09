using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using KOMA.Data;
using KOMA.Models;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Services;

public class ReservationInput
{
    [Display(Name = "Table")]
    public int RestaurantTableId { get; set; }
    [Display(Name = "Dining area preference")] public string? PreferredZone { get; set; }
    [Required, StringLength(100), Display(Name = "Customer name")]
    public string CustomerName { get; set; } = "";
    [Required, Phone, StringLength(30), Display(Name = "Contact number")]
    public string ContactNumber { get; set; } = "";
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    [DataType(DataType.Date)] public DateOnly Date { get; set; }
    [DataType(DataType.Time), Display(Name = "Start time")]
    public TimeOnly Time { get; set; }
    [Range(1, 20), Display(Name = "Number of guests")]
    public int GuestCount { get; set; } = 1;
    [StringLength(500), Display(Name = "Special requests / dining preferences")]
    public string? SpecialRequests { get; set; }
    // New bookings have no concurrency token; existing bookings must match it in SaveAsync.
    public string? RowVersion { get; set; }
}

public static class ReservationRules
{
    public const int CallMinutes = 15;
    public const int GraceMinutes = 30;
    public const int SpacingMinutes = 120;
    public const int TurnaroundMinutes = 15;
    public const int SlotMinutes = 60;
    public static DateTime MalaysiaNow => DemoClock.Shared.MalaysiaNow;
    public static IReadOnlyList<TimeOnly> Slots(RestaurantSettings settings)
    {
        var slots = new List<TimeOnly>();
        var opening = settings.OpeningTime.ToTimeSpan().TotalMinutes;
        var closing = settings.ClosingTime.ToTimeSpan().TotalMinutes;
        for (var minute = Math.Ceiling(opening / 60) * 60; minute + SlotMinutes + TurnaroundMinutes <= closing; minute += SlotMinutes)
            slots.Add(TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute)));
        return slots;
    }
    public static string? Validate(ReservationInput input, int capacity, RestaurantSettings settings, DateTime now, bool keepActiveSlot = false)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), errors, true))
            return errors[0].ErrorMessage;
        if (input.Date == default || input.Date == DateOnly.MaxValue) return "Choose a valid reservation date.";
        if (!keepActiveSlot && input.Date.ToDateTime(input.Time) <= now) return "Choose a future date and time.";
        if (!keepActiveSlot && !Slots(settings).Contains(input.Time)) return "Choose a one-hour slot within the restaurant's opening hours.";
        if (input.GuestCount > capacity) return $"This table seats at most {capacity} guests.";
        return null;
    }
    public static bool Overlaps(DateTime start, DateTime end, DateTime otherStart, DateTime otherEnd)
        => start < otherEnd && end > otherStart;
}

public record BookingResult(int? Id, string? Error, string? SuggestedZone = null);

// Shared business rules. Page handlers do not write reservations directly.
public class ReservationService(ApplicationDbContext context, KOMA.Services.Email.BookingEmailService emails)
{
    private readonly ApplicationDbContext _context = context;
    private static bool IsStaff(ClaimsPrincipal actor) => actor.IsInRole("Staff") || actor.IsInRole("Administrator");

    internal static IQueryable<Reservation> BlockingReservations(ApplicationDbContext db, int? excludeId, DateTime scheduled, DateTime occupiedFrom, DateTime diningEnd)
    {
        var earliest = scheduled.AddMinutes(-ReservationRules.SpacingMinutes);
        var latest = scheduled.AddMinutes(ReservationRules.SpacingMinutes);
        var ready = diningEnd.AddMinutes(ReservationRules.TurnaroundMinutes);
        var previousEnd = occupiedFrom.AddMinutes(-ReservationRules.TurnaroundMinutes);
        return db.Reservations.Where(r => r.Id != (excludeId ?? 0) &&
                r.Status != ReservationStatus.Cancelled && r.Status != ReservationStatus.NoShow &&
                ((r.StartTime > earliest && r.StartTime < latest) || (r.StartTime < ready && r.EndTime > previousEnd)));
    }

    internal static IQueryable<RestaurantTable> AvailableTables(ApplicationDbContext db, int guests,
        int? excludeId, DateTime scheduled, DateTime occupiedFrom, DateTime diningEnd)
    {
        var blockers = BlockingReservations(db, excludeId, scheduled, occupiedFrom, diningEnd);
        var ready = diningEnd.AddMinutes(ReservationRules.TurnaroundMinutes);
        var previousEnd = occupiedFrom.AddMinutes(-ReservationRules.TurnaroundMinutes);
        return db.RestaurantTables.Include(t => t.DiningArea)
            .Where(t => t.IsActive && t.DiningArea!.IsActive && t.Capacity >= guests)
            .Where(t => !blockers.Any(r => r.RestaurantTableId == t.Id))
            .Where(t => !db.TableServiceSessions.Any(s => s.RestaurantTableId == t.Id && s.ReservationId == null &&
                s.StartTime < ready && s.EndTime > previousEnd));
    }

    public async Task<BookingResult> SaveAsync(ReservationInput input, int? id, ClaimsPrincipal actor)
    {
        var userId = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null || (!IsStaff(actor) && !actor.IsInRole("Customer")))
            return new(null, "You do not have permission to make this booking.");

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        // A transaction-owned SQL Server lock serializes booking writes across app instances.
        // All future customer booking handlers must use this service too.
        await LockAsync();
        await ArrivalService.ExpireAsync(_context, ReservationRules.MalaysiaNow);
        Reservation? booking = null;
        if (id.HasValue)
        {
            booking = await _context.Reservations.SingleOrDefaultAsync(r => r.Id == id.Value);
            if (booking == null) return new(null, "Reservation not found.");
            if (!IsStaff(actor) && booking.CustomerUserId != userId) return new(null, "You cannot change this reservation.");
            if (!IsStaff(actor) && await _context.TableServiceSessions.AnyAsync(s => s.ReservationId == id.Value))
                return new(null, "This reservation has already been checked in. Please contact staff to make changes.");
            if (booking.Status is ReservationStatus.Cancelled or ReservationStatus.NoShow) return new(null, "Cancelled reservations cannot be edited.");
            if (booking.StartTime <= ReservationRules.MalaysiaNow &&
                (!IsStaff(actor) || booking.EndTime <= ReservationRules.MalaysiaNow)) return new(null, "Past or started reservations cannot be changed.");
            if (Convert.ToBase64String(booking.RowVersion) != input.RowVersion) return new(null, "This booking changed. Reload the page before editing again.");
        }
        var settings = await _context.RestaurantSettings.FirstOrDefaultAsync();
        if (settings == null) return new(null, "Restaurant opening hours have not been configured.");
        var error = ReservationRules.Validate(input, int.MaxValue, settings, ReservationRules.MalaysiaNow, IsStaff(actor) && booking?.StartTime == input.Date.ToDateTime(input.Time));
        if (error != null) return new(null, error);
        var start = input.Date.ToDateTime(input.Time);
        var end = booking?.StartTime == start ? booking.EndTime : start.AddMinutes(ReservationRules.SlotMinutes);
        // Exclude tables occupied by either reservations or walk-ins.
        var available = AvailableTables(_context, input.GuestCount, id, start, start, end);

        var linkedSession = id.HasValue
            ? await _context.TableServiceSessions
                .SingleOrDefaultAsync(s => s.ReservationId == id.Value)
            : null;

        RestaurantTable? table;

        if (IsStaff(actor))
        {
            // Staff chooses a specific table from the floor layout.
            table = await available.SingleOrDefaultAsync(t =>
                t.Id == input.RestaurantTableId);

            if (table == null)
            {
                return new(
                    null,
                    "That table is unavailable or too small for your party. " +
                    "Choose another table or time.");
            }
        }
        else
        {
            // Customers choose a dining area; the system assigns a table.
            if (string.IsNullOrWhiteSpace(input.PreferredZone) ||
                !await _context.DiningAreas.AnyAsync(a =>
                    a.IsActive &&
                    a.Zone == input.PreferredZone))
            {
                return new(null, "Choose a dining area preference.");
            }

            if (linkedSession != null)
            {
                // Keep the table when an existing booking already has a visit.
                table = await available.SingleOrDefaultAsync(t =>
                    t.Id == linkedSession.RestaurantTableId &&
                    t.DiningArea!.Zone == input.PreferredZone);

                if (table == null)
                {
                    return new(
                        null,
                        "This booking has a linked dining visit. " +
                        "Contact staff to change its table or dining area.");
                }
            }
            else
            {
                table = await available
                    .Where(t => t.DiningArea!.Zone == input.PreferredZone)
                    .OrderBy(t => t.Capacity)
                    .ThenBy(t => t.Id)
                    .FirstOrDefaultAsync();

                if (table == null)
                {
                    var alternative = await available
                        .OrderBy(t => t.Capacity)
                        .ThenBy(t => t.Id)
                        .FirstOrDefaultAsync();

                    if (alternative == null)
                    {
                        return new(
                            null,
                            "No suitable tables are available for your party " +
                            "at this time. Please change the date, time " +
                            "or number of guests.");
                    }

                    return new(
                        null,
                        $"No suitable tables are available in {input.PreferredZone} " +
                        $"for your party at this time. We can offer " +
                        $"{alternative.DiningArea!.Zone} instead. " +
                        "Your reservation has not been saved.",
                        alternative.DiningArea.Zone);
                }
            }
        }

        // Protect the table and time of a visit already linked to the booking.
        if (linkedSession != null && !IsStaff(actor) &&
            (linkedSession.RestaurantTableId != table.Id ||
             linkedSession.StartTime != start ||
             linkedSession.EndTime != end))
        {
            return new(
                null,
                "This booking has a linked dining visit. " +
                "Its table and time cannot be changed here.");
        }
        // Booking emails (Services/Email): remember the old details so the Updated email can show old -> new.
        var isNew = booking == null;
        var before = booking == null ? null : new { booking.StartTime, booking.GuestCount, booking.RestaurantTableId,
            booking.CustomerName, booking.ContactNumber, booking.Email, booking.SpecialRequests };
        if (booking == null)
        {
            booking = new Reservation { CreatedByUserId = userId, CustomerUserId = actor.IsInRole("Customer") ? userId : null, Status = ReservationStatus.Confirmed };
            _context.Reservations.Add(booking);
        }
        booking.RestaurantTableId = table.Id;
        booking.CustomerName = input.CustomerName.Trim();
        booking.ContactNumber = input.ContactNumber.Trim();
        booking.Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
        booking.GuestCount = input.GuestCount;
        booking.SpecialRequests = input.SpecialRequests?.Trim();
        if (isNew || booking.StartTime != start)
        {
            booking.HoldUntil = start.AddMinutes(ReservationRules.GraceMinutes);
            booking.ExpectedArrival = null;
            booking.ContactAttemptAt = null;
        }
        booking.StartTime = start;
        booking.EndTime = end;
        // Keep the linked visit's customer information up to date.
        if (linkedSession != null)
        {
            linkedSession.StartTime = start;
            linkedSession.EndTime = end;
            linkedSession.RestaurantTableId = table.Id;
            linkedSession.CustomerName = booking.CustomerName;
            linkedSession.ContactNumber = booking.ContactNumber;
            linkedSession.GuestCount = booking.GuestCount;
        }

        // Keep the customer's pre-order in sync with the booking.
        if (id.HasValue)
        {
            var sessionId = linkedSession?.Id;
            await _context.Orders.Where(o => o.ReservationId == booking.Id ||
                (sessionId.HasValue && o.TableServiceSessionId == sessionId))
                .ExecuteUpdateAsync(u => u.SetProperty(o => o.RestaurantTableId, (int?)table.Id)
                    .SetProperty(o => o.CustomerName, booking.CustomerName));
        }
        if (isNew) await emails.QueueConfirmedAsync(booking);
        else if (before != null)
        {
            var changes = new Dictionary<string, (string, string)>();
            if (before.CustomerName != booking.CustomerName) changes["Name"] = (before.CustomerName, booking.CustomerName);
            if (before.ContactNumber != booking.ContactNumber) changes["Contact number"] = (before.ContactNumber, booking.ContactNumber);
            if (before.Email != booking.Email) changes["Email"] = (before.Email ?? "None", booking.Email ?? "None");
            if (before.SpecialRequests != booking.SpecialRequests) changes["Special requests"] = (before.SpecialRequests ?? "None", booking.SpecialRequests ?? "None");
            if (before.StartTime.Date != booking.StartTime.Date)
                changes["Date"] = (KOMA.Services.Email.BookingEmailTemplates.Day(before.StartTime), KOMA.Services.Email.BookingEmailTemplates.Day(booking.StartTime));
            if (before.StartTime.TimeOfDay != booking.StartTime.TimeOfDay)
                changes["Time"] = (KOMA.Services.Email.BookingEmailTemplates.Time(before.StartTime), KOMA.Services.Email.BookingEmailTemplates.Time(booking.StartTime));
            if (before.GuestCount != booking.GuestCount)
                changes["Guests"] = (before.GuestCount.ToString(), booking.GuestCount.ToString());
            if (before.RestaurantTableId != booking.RestaurantTableId)
            {
                var oldTable = await _context.RestaurantTables.Include(t => t.DiningArea).FirstOrDefaultAsync(t => t.Id == before.RestaurantTableId);
                static string Seat(RestaurantTable? t) => t == null ? "—" : (t.DiningArea == null ? "" : t.DiningArea.Name + " · ") + "Table " + t.TableNumber;
                changes["Seating"] = (Seat(oldTable), Seat(table));
            }
            await emails.QueueUpdatedAsync(booking, changes, byCustomer: !IsStaff(actor));
        }
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(booking.Id, null);
    }

    public async Task<string?> ChangeStatusAsync(int id, string version, bool confirm, ClaimsPrincipal actor)
    {
        var userId = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return "Please log in again.";
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockAsync();
        var booking = await _context.Reservations.SingleOrDefaultAsync(r => r.Id == id);
        if (booking == null) return "Reservation not found.";
        if (!IsStaff(actor) && (confirm || !actor.IsInRole("Customer") || booking.CustomerUserId != userId))
            return "You do not have permission to change this booking.";
        if (Convert.ToBase64String(booking.RowVersion) != version) return "This booking changed. Review its latest details and try again.";
        if (booking.StartTime <= ReservationRules.MalaysiaNow) return "Past or started reservations cannot be changed.";
        if (booking.Status == ReservationStatus.Cancelled) return "This reservation is already cancelled.";
        if (!IsStaff(actor) && await _context.TableServiceSessions.AnyAsync(s => s.ReservationId == id))
            return "This reservation has already been checked in. Please contact staff.";
        if (confirm && booking.Status != ReservationStatus.Pending) return "Only pending reservations can be confirmed.";
        if (!confirm)
        {
            var hasFoodOrders = await _context.Orders.AnyAsync(o =>
                o.TableServiceSession != null &&
                o.TableServiceSession.ReservationId == id &&
                o.Status != OrderStatus.Cancelled);

            if (hasFoodOrders)
            {
                return "This booking has food orders. " +
                       "Ask the administrator to resolve those orders " +
                       "before cancelling the booking.";
            }
        }
        booking.Status = confirm ? ReservationStatus.Confirmed : ReservationStatus.Cancelled;
        if (!confirm)
        {
            var preOrder = await _context.Orders.SingleOrDefaultAsync(o => o.ReservationId == id);
            if (preOrder != null && preOrder.Status == OrderStatus.AwaitingArrival)
                preOrder.Status = OrderStatus.Cancelled;
        }
        // Booking emails: confirmed by staff, or cancelled (by the customer or by staff)
        if (confirm) await emails.QueueConfirmedAsync(booking);
        else await emails.QueueCancelledAsync(booking, byCustomer: !IsStaff(actor));
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return null;
    }

    private Task LockAsync() => _context.Database.ExecuteSqlRawAsync("""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource = 'KOMA.ReservationWrites',
            @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @result < 0 THROW 51000, 'Reservation service is busy. Please try again.', 1;
        """);
}

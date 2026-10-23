using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using KOMA.Data;
using KOMA.Models;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Services;

public class ReservationInput
{
    [Range(1, int.MaxValue), Display(Name = "Table")]
    public int RestaurantTableId { get; set; }
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
    public string RowVersion { get; set; } = "";
}

public static class ReservationRules
{
    public const int SlotMinutes = 60;
    public static DateTime MalaysiaNow => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Singapore Standard Time");
    public static IReadOnlyList<TimeOnly> Slots(RestaurantSettings settings)
    {
        var slots = new List<TimeOnly>();
        var opening = settings.OpeningTime.ToTimeSpan().TotalMinutes;
        var closing = settings.ClosingTime.ToTimeSpan().TotalMinutes;
        for (var minute = opening; minute + SlotMinutes <= closing; minute += SlotMinutes)
            slots.Add(TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute)));
        return slots;
    }
    public static string? Validate(ReservationInput input, int capacity, RestaurantSettings settings, DateTime now)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), errors, true))
            return errors[0].ErrorMessage;
        if (input.Date == default || input.Date == DateOnly.MaxValue) return "Choose a valid reservation date.";
        if (input.Date.ToDateTime(input.Time) <= now) return "Choose a future date and time.";
        if (!Slots(settings).Contains(input.Time)) return "Choose a one-hour slot within the restaurant's opening hours.";
        if (input.GuestCount > capacity) return $"This table seats at most {capacity} guests.";
        return null;
    }
    public static bool Overlaps(DateTime start, DateTime end, DateTime otherStart, DateTime otherEnd)
        => start < otherEnd && end > otherStart;
}

public record BookingResult(int? Id, string? Error);

// Shared business rules. Page handlers do not write reservations directly.
public class ReservationService(ApplicationDbContext context)
{
    private readonly ApplicationDbContext _context = context;
    private static bool IsStaff(ClaimsPrincipal actor) => actor.IsInRole("Staff") || actor.IsInRole("Administrator");

    public async Task<BookingResult> SaveAsync(ReservationInput input, int? id, ClaimsPrincipal actor)
    {
        var userId = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null || (!IsStaff(actor) && !actor.IsInRole("Customer")))
            return new(null, "You do not have permission to make this booking.");

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        // A transaction-owned SQL Server lock serializes booking writes across app instances.
        // All future customer booking handlers must use this service too.
        await LockAsync();
        Reservation? booking = null;
        if (id.HasValue)
        {
            booking = await _context.Reservations.SingleOrDefaultAsync(r => r.Id == id.Value);
            if (booking == null) return new(null, "Reservation not found.");
            if (!IsStaff(actor) && booking.CustomerUserId != userId) return new(null, "You cannot change this reservation.");
            if (booking.Status == ReservationStatus.Cancelled) return new(null, "Cancelled reservations cannot be edited.");
            if (booking.StartTime <= ReservationRules.MalaysiaNow) return new(null, "Past or started reservations cannot be changed.");
            if (Convert.ToBase64String(booking.RowVersion) != input.RowVersion) return new(null, "This booking changed. Reload the page before editing again.");
        }
        var table = await _context.RestaurantTables.Include(t => t.DiningArea)
            .SingleOrDefaultAsync(t => t.Id == input.RestaurantTableId);
        if (table == null || !table.IsActive || table.DiningArea?.IsActive != true)
            return new(null, "Select an active table in an active dining area.");
        var settings = await _context.RestaurantSettings.FirstOrDefaultAsync();
        if (settings == null) return new(null, "Restaurant opening hours have not been configured.");
        var error = ReservationRules.Validate(input, table.Capacity, settings, ReservationRules.MalaysiaNow);
        if (error != null) return new(null, error);
        var start = input.Date.ToDateTime(input.Time);
        var end = start.AddMinutes(ReservationRules.SlotMinutes);
        var overlapsWalkIn = await _context.TableServiceSessions
            .AnyAsync(s =>
                s.RestaurantTableId == table.Id &&
                s.ReservationId == null &&
                s.StartTime < end &&
                s.EndTime > start);

        if (overlapsWalkIn)
        {
            return new(
                null,
                "This table has a walk-in visit during the selected slot.");
        }

        var linkedSession = id.HasValue
            ? await _context.TableServiceSessions
                .SingleOrDefaultAsync(s => s.ReservationId == id.Value)
            : null;

        if (linkedSession != null &&
            (linkedSession.RestaurantTableId != table.Id ||
             linkedSession.StartTime != start ||
             linkedSession.EndTime != end))
        {
            return new(
                null,
                "This booking has a linked dining visit. " +
                "Its table and time cannot be changed here.");
        }
        if (await _context.Reservations.AnyAsync(r => r.RestaurantTableId == table.Id && r.Id != (id ?? 0)
            && r.Status != ReservationStatus.Cancelled && r.StartTime < end && r.EndTime > start))
            return new(null, "That table is already booked for this slot. Choose another table or time.");
        if (booking == null)
        {
            booking = new Reservation { CreatedByUserId = userId, CustomerUserId = actor.IsInRole("Customer") ? userId : null };
            _context.Reservations.Add(booking);
        }
        booking.RestaurantTableId = table.Id;
        booking.CustomerName = input.CustomerName.Trim();
        booking.ContactNumber = input.ContactNumber.Trim();
        booking.Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
        booking.GuestCount = input.GuestCount;
        booking.SpecialRequests = input.SpecialRequests?.Trim();
        booking.StartTime = start;
        booking.EndTime = end;
        if (linkedSession != null)
        {
            linkedSession.CustomerName = booking.CustomerName;
            linkedSession.ContactNumber = booking.ContactNumber;
            linkedSession.GuestCount = booking.GuestCount;
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

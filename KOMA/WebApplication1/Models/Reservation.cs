using System.ComponentModel.DataAnnotations;

namespace KOMA.Models;

public enum ReservationStatus { Pending, Confirmed, Cancelled, NoShow }

public class Reservation
{
    public int Id { get; set; }
    public int RestaurantTableId { get; set; }
    public RestaurantTable RestaurantTable { get; set; } = null!;
    // Optional account link: staff may also record telephone / walk-in bookings.
    public string? CustomerUserId { get; set; }
    public ApplicationUser? CustomerUser { get; set; }
    [Required, StringLength(100)] public string CustomerName { get; set; } = "";
    [Required, StringLength(30)] public string ContactNumber { get; set; } = "";
    [StringLength(256)] public string? Email { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? HoldUntil { get; set; }
    public DateTime? ExpectedArrival { get; set; }
    public DateTime? ContactAttemptAt { get; set; }
    public DateTime EndTime { get; set; }
    [Range(1, 20)] public int GuestCount { get; set; }
    [StringLength(500)] public string? SpecialRequests { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Confirmed;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedByUserId { get; set; } = "";
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

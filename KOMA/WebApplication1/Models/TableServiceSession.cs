using System;
using System.ComponentModel.DataAnnotations;

namespace KOMA.Models;

public class TableServiceSession
{
    public int Id { get; set; }

    public int RestaurantTableId { get; set; }

    public RestaurantTable RestaurantTable { get; set; } = null!;

    // Null means this visit is a walk-in without a reservation.
    public int? ReservationId { get; set; }

    public Reservation? Reservation { get; set; }

    [Required]
    [StringLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    [StringLength(30)]
    public string? ContactNumber { get; set; }

    [Range(1, 20)]
    public int GuestCount { get; set; }

    // Malaysia local date and time, matching existing reservations.
    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }
}
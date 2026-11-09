using System.ComponentModel.DataAnnotations;
namespace KOMA.Models;

public class Feedback
{
    public int Id { get; set; }
    [Range(1, 5)] public int Rating { get; set; }
    [Required, StringLength(2000)] public string Comment { get; set; } = "";
    [StringLength(100)] public string Name { get; set; } = "Visitor";
    [StringLength(256)] public string? Email { get; set; }
    [StringLength(450)] public string? CustomerUserId { get; set; }
    public int? ReservationId { get; set; }
    public Reservation? Reservation { get; set; }
    public int? TableServiceSessionId { get; set; }
    public TableServiceSession? TableServiceSession { get; set; }
    [StringLength(20)] public string Source { get; set; } = "Website";
    public DateTime CreatedAtUtc { get; set; } = KOMA.Services.DemoClock.Shared.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

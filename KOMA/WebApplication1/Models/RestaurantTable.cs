using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KOMA.Models
{
    // Owned by Engineer A (definition/CRUD). Read by Engineer B's reservation-availability
    // logic and Engineer C's booking pages - keep the shape stable once those depend on it.
    public class RestaurantTable
    {
        public int Id { get; set; }
        public Guid? TabletDeviceId { get; set; }

        [Required, StringLength(20)]
        public string TableNumber { get; set; } = string.Empty;

        [Range(1, 20)]
        public int Capacity { get; set; }

        // Stored as an int in the database (Square = 0, the default for existing rows).
        [Display(Name = "Table Shape")]
        public TableShape Shape { get; set; } = TableShape.Square;

        public bool IsActive { get; set; } = true;

        [ForeignKey(nameof(DiningArea))]
        public int DiningAreaId { get; set; }
        public DiningArea? DiningArea { get; set; }

        // Position and size on the floor layout, in metres from the floor's top-left corner
        // (same coordinates as the dining area, so the table always sits inside its area).
        public double LayoutX { get; set; }
        public double LayoutY { get; set; }
        public double LayoutWidth { get; set; } = 1;
        public double LayoutHeight { get; set; } = 1;

        // TODO (Engineer B, Intermediate tier): availability is computed from Reservations,
        // not stored here - don't add an "IsAvailable" flag, it will go stale.
    }

    public enum TableShape
    {
        Square = 0,
        Round = 1,
        Rectangle = 2,
        Booth = 3,
        Counter = 4
    }
}


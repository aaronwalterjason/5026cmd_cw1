using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RROMS.Models.Admin
{
    // Owned by Engineer A (definition/CRUD). Read by Engineer B's reservation-availability
    // logic and Engineer C's booking pages - keep the shape stable once those depend on it.
    public class RestaurantTable
    {
        public int Id { get; set; }

        [Required, StringLength(20)]
        public string TableNumber { get; set; } = string.Empty;

        [Range(1, 20)]
        public int Capacity { get; set; }

        public bool IsActive { get; set; } = true;

        [ForeignKey(nameof(DiningArea))]
        public int DiningAreaId { get; set; }
        public DiningArea? DiningArea { get; set; }

        // TODO (Engineer B, Intermediate tier): availability is computed from Reservations,
        // not stored here - don't add an "IsAvailable" flag, it will go stale.
    }
}

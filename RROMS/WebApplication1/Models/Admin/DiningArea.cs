using System.ComponentModel.DataAnnotations;

namespace RROMS.Models.Admin
{
    // Owned by Engineer A. A physical area of the restaurant (e.g. "Main Hall", "Rooftop", "Private Room").
    // RestaurantTable rows below belong to one DiningArea each.
    public class DiningArea
    {
        public int Id { get; set; }

        [Required, StringLength(80)]
        public string Name { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }

        // e.g. "Indoor", "Outdoor", "Private" - used for filtering on the customer-facing menu/booking pages.
        [StringLength(40)]
        public string? Zone { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<RestaurantTable> Tables { get; set; } = new List<RestaurantTable>();
    }
}

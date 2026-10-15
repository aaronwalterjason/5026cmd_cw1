using System.ComponentModel.DataAnnotations;

namespace KOMA.Models
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

        // Picked from ZoneOptions on the admin form - used for filtering on the customer-facing menu/booking pages.
        [Required(ErrorMessage = "Please choose a zone.")]
        [StringLength(40)]
        public string Zone { get; set; } = "Indoor";

        // Picked from FloorOptions on the admin form.
        [Required(ErrorMessage = "Please choose a floor.")]
        [StringLength(30)]
        public string Floor { get; set; } = "Ground Floor";

        public bool IsActive { get; set; } = true;

        public ICollection<RestaurantTable> Tables { get; set; } = new List<RestaurantTable>();

        // Fixed choices shown in the Zone / Floor dropdowns. Add to these lists to offer more options.
        public static readonly string[] ZoneOptions = { "Indoor", "Outdoor", "Private Room", "Counter Seating" };
        public static readonly string[] FloorOptions = { "Ground Floor", "Level 1", "Level 2", "Rooftop" };
    }
}

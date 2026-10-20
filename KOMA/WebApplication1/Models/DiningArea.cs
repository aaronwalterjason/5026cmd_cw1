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

        // The floor this area is drawn on. Set by the layout designer.
        [Display(Name = "Floor")]
        public int? FloorLayoutId { get; set; }
        public FloorLayout? FloorLayout { get; set; }

        // Position and size on the floor layout, in metres from the floor's top-left corner.
        public double LayoutX { get; set; }
        public double LayoutY { get; set; }
        public double LayoutWidth { get; set; } = 6;
        public double LayoutHeight { get; set; } = 4;

        public bool IsActive { get; set; } = true;

        public ICollection<RestaurantTable> Tables { get; set; } = new List<RestaurantTable>();

        // Fixed choices shown in the Zone dropdown. Add to this list to offer more options
        // (and give the new zone a colour in wwwroot/css/site.css: .zone-...).
        public static readonly string[] ZoneOptions = { "Indoor", "Outdoor", "Private Room", "Counter Seating" };
    }
}

using System.ComponentModel.DataAnnotations;

namespace RROMS.Models.Admin
{
    // Single-row table holding restaurant-wide configuration (opening hours, announcement banner).
    // Owned by Engineer A. Read the one row (there's only ever one); SeedData creates it if missing.
    public class RestaurantSettings
    {
        public int Id { get; set; }

        public TimeOnly OpeningTime { get; set; } = new(10, 0);

        public TimeOnly ClosingTime { get; set; } = new(22, 0);

        [StringLength(300)]
        public string? AnnouncementText { get; set; }
    }
}

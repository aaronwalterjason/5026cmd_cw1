using System.ComponentModel.DataAnnotations;

namespace KOMA.Models
{
    // Owned by Engineer A. One floor of the restaurant (e.g. "Ground Floor", "Rooftop") drawn as a
    // rectangle in metres. Dining areas are drawn inside it, and tables inside the dining areas,
    // using the layout designer (Pages/Admin/Layouts/Designer).
    public class FloorLayout
    {
        public int Id { get; set; }

        [Required, StringLength(40)]
        [Display(Name = "Floor name")]
        public string Name { get; set; } = string.Empty;

        // Used to sort floors: 0 = ground floor, 1 = level 1, -1 = basement, etc.
        [Range(-5, 50)]
        [Display(Name = "Floor level")]
        public int Level { get; set; }

        // Size of the floor in metres (drawn by dragging on the Create/Edit page).
        [Range(MinSize, MaxWidth)]
        [Display(Name = "Width (m)")]
        public int Width { get; set; } = 30;

        [Range(MinSize, MaxHeight)]
        [Display(Name = "Length (m)")]
        public int Height { get; set; } = 20;

        [StringLength(200)]
        public string? Description { get; set; }

        public ICollection<DiningArea> DiningAreas { get; set; } = new List<DiningArea>();

        public const int MinSize = 5;
        public const int MaxWidth = 60;
        public const int MaxHeight = 40;
    }
}

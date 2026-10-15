using System.ComponentModel.DataAnnotations;

namespace KOMA.Models
{
    // Owned by Engineer A. e.g. "Starters", "Mains", "Desserts", "Beverages".
    public class MenuCategory
    {
        public int Id { get; set; }

        [Required, StringLength(60)]
        public string Name { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<MenuItem> Items { get; set; } = new List<MenuItem>();
    }
}

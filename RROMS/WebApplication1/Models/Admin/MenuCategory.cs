using System.ComponentModel.DataAnnotations;

namespace RROMS.Models.Admin
{
    // Owned by Engineer A. e.g. "Starters", "Mains", "Desserts", "Beverages".
    public class MenuCategory
    {
        public int Id { get; set; }

        [Required, StringLength(60)]
        public string Name { get; set; } = string.Empty;

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<MenuItem> Items { get; set; } = new List<MenuItem>();
    }
}

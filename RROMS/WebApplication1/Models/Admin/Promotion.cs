using System.ComponentModel.DataAnnotations;

namespace RROMS.Models.Admin
{
    // Advanced-tier feature. Owned by Engineer A.
    public class Promotion
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Title { get; set; } = string.Empty;

        [Range(0, 100)]
        public decimal DiscountPercent { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
    }
}

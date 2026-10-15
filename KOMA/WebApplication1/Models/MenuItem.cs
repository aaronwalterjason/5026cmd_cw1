using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KOMA.Models
{
    // Owned by Engineer A (pricing/availability/promotions). Read by Engineer C's
    // menu-browsing and ordering pages - keep field names stable once C depends on them.
    public class MenuItem
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Description { get; set; }

        [Column(TypeName = "decimal(8,2)")]
        [Display(Name = "Price (RM)")]
        [Required(ErrorMessage = "Please enter a price.")]
        [Range(0.01, 9999.99, ErrorMessage = "Enter a price between RM 0.01 and RM 9,999.99.")]
        public decimal Price { get; set; }

        public bool IsAvailable { get; set; } = true;
        public bool IsRecommended { get; set; }
        public bool IsSeasonal { get; set; }

        [ForeignKey(nameof(MenuCategory))]
        public int MenuCategoryId { get; set; }
        public MenuCategory? MenuCategory { get; set; }

        // Advanced tier: link to Promotion for discounted pricing / "featured" flag.
        public int? PromotionId { get; set; }
        public Promotion? Promotion { get; set; }

        public string? ImageUrl { get; set; }
    }
}


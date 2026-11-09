using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KOMA.Models
{
    // Owned by Engineer B. One line on an order, e.g. "2 x Salmon Sashimi Platter".
    public class OrderItem
    {
        public int Id { get; set; }

        public int OrderId { get; set; }
        public Order? Order { get; set; }

        // Link to the dish while it still exists. Becomes null if the dish is later deleted from the menu
        // (the order line keeps ItemName and UnitPrice, so the customer's history is unchanged).
        public int? MenuItemId { get; set; }
        public MenuItem? MenuItem { get; set; }

        // Copied from MenuItem.Name when ordered - like UnitPrice - so past orders keep the name the
        // customer actually ordered, even if the dish is renamed or deleted afterwards.
        [Required, StringLength(100)]
        [Display(Name = "Dish")]
        public string ItemName { get; set; } = string.Empty;

        [Range(1, 99)]
        public int Quantity { get; set; } = 1;

        // Copied from MenuItem.Price when ordered, so history keeps the price actually charged.
        [Column(TypeName = "decimal(8,2)")]
        [Display(Name = "Unit price (RM)")]
        public decimal UnitPrice { get; set; }

        // Promotion on this line: the normal menu price and the promotion's name (both empty = full price).
        [Column(TypeName = "decimal(8,2)")] public decimal? ListPrice { get; set; }
        [StringLength(100)] public string? PromotionTitle { get; set; }

        // Where this dish is: Preparing -> Ready (plated, waiting at the pass) -> Served.
        // Served can be confirmed by staff/admin (Admin > Orders) or by the customer on the table tablet.
        public DishStatus Status { get; set; } = DishStatus.Preparing;
        public DateTime? ReadyAt { get; set; }
        public DateTime? ServedAt { get; set; }
        [StringLength(20)]
        public string? ServedVia { get; set; }   // "Staff" or "Tablet"

        [NotMapped]
        public decimal LineTotal => UnitPrice * Quantity;

        // Name to show on screens: the saved name (falls back to the dish for any old row without one).
        [NotMapped]
        public string DishName => !string.IsNullOrWhiteSpace(ItemName) ? ItemName : MenuItem?.Name ?? "(removed item)";
    }

    public enum DishStatus
    {
        Preparing = 0,
        [Display(Name = "Ready to serve")] Ready = 1,
        Served = 2
    }
}

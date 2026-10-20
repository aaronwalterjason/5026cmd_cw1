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

        public int MenuItemId { get; set; }
        public MenuItem? MenuItem { get; set; }

        [Range(1, 99)]
        public int Quantity { get; set; } = 1;

        // Copied from MenuItem.Price when ordered, so history keeps the price actually charged.
        [Column(TypeName = "decimal(8,2)")]
        [Display(Name = "Unit price (RM)")]
        public decimal UnitPrice { get; set; }

        [NotMapped]
        public decimal LineTotal => UnitPrice * Quantity;
    }
}

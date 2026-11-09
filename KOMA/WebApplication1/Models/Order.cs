using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KOMA.Models
{
    // Owned by Engineer B. One order = one customer's food/drink order for a dining session.
    // Pre-orders and tablet orders both end up here; staff move it through OrderStatus.
    public class Order
    {
        public int Id { get; set; }
        public int? ReservationId { get; set; }
        public Reservation? Reservation { get; set; }

        // Shown to staff/customers as e.g. "KOMA-000042".
        [NotMapped]
        public string OrderNumber => $"KOMA-{Id:D6}";

        // How the order was placed - shown and filtered on the admin Orders page.
        [Display(Name = "Order type")]
        public OrderType OrderType { get; set; } = OrderType.WalkIn;

        // Linked account for online pre-orders and table-tablet orders. Null for walk-ins without an account.
        public string? CustomerId { get; set; }
        public ApplicationUser? Customer { get; set; }

        // Name shown on the order. Copied from the customer's account when they order online or by tablet;
        // typed in by staff for walk-ins. Stored on the order so it stays correct even if the account changes.
        [Required, StringLength(100)]
        [Display(Name = "Customer name")]
        public string CustomerName { get; set; } = string.Empty;

        public int? RestaurantTableId { get; set; }
        public RestaurantTable? RestaurantTable { get; set; }
        // Links this order to a specific customer visit at the table.
        public int? TableServiceSessionId { get; set; }

        public TableServiceSession? TableServiceSession { get; set; }

        [Display(Name = "Ordered at")]
        public DateTime OrderedAt { get; set; } = KOMA.Services.DemoClock.Shared.MalaysiaNow;

        public OrderStatus Status { get; set; } = OrderStatus.Pending;

        [Display(Name = "Payment status")]
        public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

        [Display(Name = "Payment method")]
        public PaymentMethod? PaymentMethod { get; set; }

        // Revenue reports use this date, so a sale counts on the day it was paid.
        [Display(Name = "Paid at")]
        public DateTime? PaidAt { get; set; }

        // Stored (not recalculated) so past totals don't change if menu prices change later.
        [Column(TypeName = "decimal(10,2)")]
        [Display(Name = "Total (RM)")]
        public decimal TotalAmount { get; set; }

        [StringLength(300)]
        public string? Notes { get; set; }

        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    }

    public enum OrderType
    {
        [Display(Name = "Walk-in")] WalkIn = 0,                    // staff take the order for a guest who came in without booking
        [Display(Name = "Online Pre-order")] OnlinePreOrder = 1,   // ordered online with a reservation, before arriving
        [Display(Name = "Table Tablet")] TableTablet = 2           // added from the table tablet during the meal
    }

    public enum OrderStatus
    {
        [Display(Name = "Pending confirmation")]
        Pending = 0,

        Preparing = 1,

        [Display(Name = "Ready to Serve")]
        ReadyToServe = 2,

        Served = 3,
        Completed = 4,
        Cancelled = 5,

        [Display(Name = "Queued for kitchen")]
        QueuedForKitchen = 6,

        [Display(Name = "Awaiting arrival")]
        AwaitingArrival = 7
    }

    public enum PaymentStatus
    {
        Unpaid = 0,
        Paid = 1,
        Refunded = 2
    }

    public enum PaymentMethod
    {
        [Display(Name = "Touch 'n Go eWallet")] TouchNGo = 0,
        [Display(Name = "Online Banking")] OnlineBanking = 1,
        Cash = 2,
        Card = 3
    }
}

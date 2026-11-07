using System.ComponentModel.DataAnnotations;

namespace KOMA.Models
{
    // Owned by Engineer B. A customer asking for staff from the table tablet ("Call staff", "Bill please", ...).
    // Shown on Admin > Orders (table tile, pop-up and the side menu count) until someone marks it Done.
    public class HelpRequest
    {
        public int Id { get; set; }

        public int? RestaurantTableId { get; set; }
        public RestaurantTable? RestaurantTable { get; set; }

        public HelpType Type { get; set; } = HelpType.CallStaff;

        [StringLength(200)]
        public string? Note { get; set; }          // what the customer typed, e.g. "Could we get more napkins?"

        public DateTime CreatedAt { get; set; }

        [StringLength(100)]
        public string? TakenBy { get; set; }       // "I'll take it" - so two people don't walk over
        public DateTime? TakenAt { get; set; }

        [StringLength(100)]
        public string? DoneBy { get; set; }
        public DateTime? DoneAt { get; set; }      // null = still waiting
    }

    public enum HelpType
    {
        [Display(Name = "Call staff")] CallStaff = 0,
        [Display(Name = "Bill please")] Bill = 1,
        Water = 2,
        Other = 3
    }
}

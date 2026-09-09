using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RROMS.Areas.Admin.Pages.Reports
{
    // Intermediate tier (Week 7-8): filtered reports - reservations, sales, popular items,
    // daily/weekly/monthly. Advanced tier (Week 9): charts + CSV/PDF/Excel export.
    // Blocked on the Reservations/Orders tables that will be added to ApplicationDbContext.
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        public DateTime FromDate { get; set; } = DateTime.Today.AddDays(-7);
        public DateTime ToDate { get; set; } = DateTime.Today;

        public void OnGet(DateTime? from, DateTime? to)
        {
            if (from.HasValue) FromDate = from.Value;
            if (to.HasValue) ToDate = to.Value;
        }
    }
}

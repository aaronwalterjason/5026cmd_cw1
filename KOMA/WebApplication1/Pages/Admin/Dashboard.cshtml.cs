using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;

namespace KOMA.Pages.Admin
{
    [Authorize(Roles = "Administrator")]
    public class DashboardModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public DashboardModel(ApplicationDbContext db) => _db = db;

        public int DiningAreaCount { get; set; }
        public int TableCount { get; set; }
        public int MenuItemCount { get; set; }
        public int ActivePromotionCount { get; set; }

        public async Task OnGetAsync()
        {
            DiningAreaCount = await _db.DiningAreas.CountAsync();
            TableCount = await _db.RestaurantTables.CountAsync();
            MenuItemCount = await _db.MenuItems.CountAsync();
            ActivePromotionCount = await _db.Promotions.CountAsync(p => p.IsActive);
        }
    }
}

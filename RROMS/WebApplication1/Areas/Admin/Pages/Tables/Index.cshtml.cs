using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models.Admin;

namespace RROMS.Areas.Admin.Pages.Tables
{
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public IList<RestaurantTable> Tables { get; set; } = new List<RestaurantTable>();

        public async Task OnGetAsync()
        {
            Tables = await _db.RestaurantTables.Include(t => t.DiningArea).OrderBy(t => t.TableNumber).ToListAsync();
        }
    }
}

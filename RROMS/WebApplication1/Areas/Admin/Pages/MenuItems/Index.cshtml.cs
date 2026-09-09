using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models.Admin;

namespace RROMS.Areas.Admin.Pages.MenuItems
{
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public IList<MenuItem> Items { get; set; } = new List<MenuItem>();

        public async Task OnGetAsync()
        {
            Items = await _db.MenuItems.Include(i => i.MenuCategory).Include(i => i.Promotion).OrderBy(i => i.Name).ToListAsync();
        }
    }
}

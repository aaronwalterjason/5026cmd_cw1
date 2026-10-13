using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin.MenuItems
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

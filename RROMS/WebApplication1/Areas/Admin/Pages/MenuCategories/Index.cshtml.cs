using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models.Admin;

namespace RROMS.Areas.Admin.Pages.MenuCategories
{
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public IList<MenuCategory> Categories { get; set; } = new List<MenuCategory>();

        public async Task OnGetAsync()
        {
            Categories = await _db.MenuCategories.OrderBy(c => c.DisplayOrder).ToListAsync();
        }
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin.MenuCategories
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

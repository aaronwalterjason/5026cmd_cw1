using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models.Admin;

namespace RROMS.Areas.Admin.Pages.DiningAreas
{
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public IList<DiningArea> DiningAreas { get; set; } = new List<DiningArea>();

        public async Task OnGetAsync()
        {
            DiningAreas = await _db.DiningAreas.Include(d => d.Tables).OrderBy(d => d.Name).ToListAsync();
        }
    }
}

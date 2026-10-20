using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin.Layouts
{
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public IList<FloorLayout> Floors { get; set; } = new List<FloorLayout>();

        public async Task OnGetAsync()
        {
            Floors = await _db.FloorLayouts.AsNoTracking()
                .Include(f => f.DiningAreas).ThenInclude(a => a.Tables)
                .OrderBy(f => f.Level).ThenBy(f => f.Name)
                .ToListAsync();
        }

        // CSS class for a zone's colour, e.g. "Private Room" -> "zone-private-room".
        public static string ZoneClass(string? zone) =>
            "zone-" + (zone ?? "indoor").Trim().ToLowerInvariant().Replace(' ', '-');

        // Percent position helpers for the small preview drawings.
        public static string Pct(double value, double total) =>
            (total <= 0 ? 0 : value / total * 100).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }
}

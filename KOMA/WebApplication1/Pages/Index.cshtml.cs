using KOMA.Data;
using KOMA.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public List<MenuItem> Dishes { get; private set; } = [];
    public async Task OnGetAsync()
    {
        Dishes = await db.MenuItems.AsNoTracking()
            .Where(i => i.IsAvailable && i.MenuCategory!.IsActive && (i.IsRecommended || i.IsSeasonal))
            .OrderBy(i => i.MenuCategory!.DisplayOrder).ThenBy(i => i.Name)
            .ToListAsync();
    }
}

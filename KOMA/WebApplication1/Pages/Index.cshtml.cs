using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public List<MenuItem> Dishes { get; private set; } = [];
    public List<Promotion> Promotions { get; private set; } = [];
    // Editable wording/photo (Admin > Website). Falls back to the original defaults if nothing is saved yet.
    public LandingPageContent Content { get; private set; } = new();
    public async Task<IActionResult> OnGetAsync()
    {
        if (User.IsInRole("Administrator"))
            return RedirectToPage("/Admin/Dashboard");

        if (User.IsInRole("Staff"))
            return RedirectToPage("/Staff/Index");

        Content = await db.LandingPageContents.AsNoTracking().FirstOrDefaultAsync() ?? new LandingPageContent();
        Dishes = await db.MenuItems.AsNoTracking()
            .Where(i => i.IsAvailable && i.MenuCategory!.IsActive && (i.IsRecommended || i.IsSeasonal))
            .OrderBy(i => i.MenuCategory!.Name).ThenBy(i => i.Name)
            .ToListAsync();

        var today = ReservationRules.MalaysiaNow.Date;
        Promotions = await db.Promotions.AsNoTracking()
            .Where(p => p.IsActive && p.ShowOnHome && p.EndDate >= today
                && p.BannerImageUrl != null && p.BannerImageUrl.Trim() != "")
            .Include(p => p.MenuItems.Where(m => m.IsAvailable && m.MenuCategory!.IsActive))
            .OrderBy(p => p.StartDate).ThenBy(p => p.Id)
            .ToListAsync();

        return Page();
    }
}

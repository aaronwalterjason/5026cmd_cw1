using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
namespace KOMA.Pages.Dining;

[Authorize(Roles = "Staff,Administrator")]
public class SetupModel(ApplicationDbContext db, TabletAccess tablet, SignInManager<ApplicationUser> signIn) : PageModel
{
    public List<RestaurantTable> Tables { get; private set; } = [];
    [BindProperty] public int TableId { get; set; }
    public async Task OnGetAsync()
    {
        Tables = await db.RestaurantTables.Include(t => t.DiningArea).Where(t => t.IsActive && t.DiningArea!.IsActive)
            .OrderBy(t => t.DiningArea!.Name).ThenBy(t => t.TableNumber).ToListAsync();
        TableId = await tablet.TableAsync(HttpContext) ?? 0;
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await tablet.PairAsync(HttpContext, TableId)) { await OnGetAsync(); ModelState.AddModelError("", "Choose an active table."); return Page(); }
        await signIn.SignOutAsync();
        return RedirectToPage("Activate");
    }
}

using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Admin.LandingPage;

// Admin > Website: edit the wording, banner photo and sections of the customer landing page (Pages/Index).
[Authorize(Roles = "Administrator")]
[RequestSizeLimit(6 * 1024 * 1024)]
public class IndexModel(ApplicationDbContext _db, MenuImageService images) : PageModel
{
    [BindProperty] public LandingPageContent Content { get; set; } = new();
    [BindProperty] public IFormFile? HeroPhoto { get; set; }
    [BindProperty] public bool RemovePhoto { get; set; }

    // Dishes shown in the two rows - chosen on the Menu page (Recommended / Seasonal switches).
    public int RecommendedCount { get; private set; }
    public int SeasonalCount { get; private set; }
    public bool IsSaved { get; private set; }   // false = nothing saved yet, page shows the defaults

    public async Task OnGetAsync()
    {
        var saved = await _db.LandingPageContents.AsNoTracking().FirstOrDefaultAsync();
        IsSaved = saved is not null;
        Content = saved ?? new LandingPageContent();
        await LoadCountsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var existing = await _db.LandingPageContents.FirstOrDefaultAsync();
        var image = existing?.HeroImageUrl;

        if (ModelState.IsValid)
        {
            if (HeroPhoto is not null)
            {
                try { image = await images.SaveAsync(HeroPhoto); }
                catch (ArgumentException ex) { ModelState.AddModelError(nameof(HeroPhoto), ex.Message); }
            }
            else if (RemovePhoto) image = null;   // back to the original banner photo
        }
        if (!ModelState.IsValid)
        {
            Content.HeroImageUrl = existing?.HeroImageUrl;
            IsSaved = existing is not null;
            await LoadCountsAsync();
            return Page();
        }

        if (existing is null)
        {
            existing = new LandingPageContent();
            _db.LandingPageContents.Add(existing);
        }
        Content.Id = existing.Id;                         // copy every field from the form onto the saved row
        _db.Entry(existing).CurrentValues.SetValues(Content);
        existing.HeroImageUrl = image;
        existing.UpdatedAt = KOMA.Services.DemoClock.Shared.MalaysiaNow;
        await _db.SaveChangesAsync();

        TempData["StatusMessage"] = "The landing page was updated.";
        return RedirectToPage();
    }

    // Put every field back to the original KOMA wording and photo.
    public async Task<IActionResult> OnPostResetAsync()
    {
        var existing = await _db.LandingPageContents.FirstOrDefaultAsync();
        if (existing is not null)
        {
            _db.LandingPageContents.Remove(existing);   // no row = the landing page uses the defaults
            await _db.SaveChangesAsync();
        }
        TempData["StatusMessage"] = "The landing page was reset to the original wording and photo.";
        return RedirectToPage();
    }

    private async Task LoadCountsAsync()
    {
        var shown = _db.MenuItems.Where(i => i.IsAvailable && i.MenuCategory!.IsActive);
        RecommendedCount = await shown.CountAsync(i => i.IsRecommended);
        SeasonalCount = await shown.CountAsync(i => i.IsSeasonal);
    }
}

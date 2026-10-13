using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin.Settings
{
    // Basic tier: operating hours. Intermediate/Advanced: announcements, promo campaign toggles.
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public TimeOnly OpeningTime { get; set; } = new(10, 0);

        [BindProperty]
        public TimeOnly ClosingTime { get; set; } = new(22, 0);

        [BindProperty]
        public string AnnouncementText { get; set; } = string.Empty;

        public async Task OnGetAsync()
        {
            var settings = await GetOrCreateSettingsAsync();
            OpeningTime = settings.OpeningTime;
            ClosingTime = settings.ClosingTime;
            AnnouncementText = settings.AnnouncementText ?? string.Empty;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var settings = await GetOrCreateSettingsAsync();
            settings.OpeningTime = OpeningTime;
            settings.ClosingTime = ClosingTime;
            settings.AnnouncementText = AnnouncementText;
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = "Settings saved.";
            return RedirectToPage();
        }

        private async Task<RestaurantSettings> GetOrCreateSettingsAsync()
        {
            var settings = await _db.RestaurantSettings.FirstOrDefaultAsync();
            if (settings is null)
            {
                settings = new RestaurantSettings();
                _db.RestaurantSettings.Add(settings);
                await _db.SaveChangesAsync();
            }
            return settings;
        }
    }
}

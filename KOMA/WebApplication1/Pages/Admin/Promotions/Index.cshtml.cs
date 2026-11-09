using System.ComponentModel.DataAnnotations;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Admin.Promotions;

// Admin > Promotions (alt D, editor design 1 "Live banner"): the promotions on the left, the chosen one open on
// the right. The banner at the top of the editor is what customers see; its title is edited right on it.
// /Admin/Promotions?id=3 opens one, ?mode=new starts a new one.
[Authorize(Roles = "Administrator")]
[RequestSizeLimit(6 * 1024 * 1024)]
public class IndexModel(ApplicationDbContext _db, MenuImageService images) : PageModel
{
    public const int HomeSlots = 3;
    public static readonly string[] Palette = { "#d0442b", "#c39143", "#42602d", "#2a83a2", "#745399", "#e0703c" };

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Mode { get; set; }

    [BindProperty] public PromoInput Input { get; set; } = new();
    [BindProperty] public IFormFile? Banner { get; set; }

    public class PromoInput
    {
        public int Id { get; set; }
        [Required(ErrorMessage = "Give the promotion a name."), StringLength(100)]
        public string Title { get; set; } = "";
        [Range(1, 90, ErrorMessage = "The discount must be between 1% and 90%.")]
        public decimal DiscountPercent { get; set; } = 20;
        public List<int> Days { get; set; } = new() { 0, 1, 2, 3, 4, 5, 6 };
        public bool AllDay { get; set; } = true;
        public int FromMin { get; set; } = 690;     // 11:30
        public int ToMin { get; set; } = 870;       // 2:30 pm
        [DataType(DataType.Date)] public DateTime StartDate { get; set; } = KOMA.Services.DemoClock.Shared.MalaysiaNow.Date;
        [DataType(DataType.Date)] public DateTime EndDate { get; set; } = KOMA.Services.DemoClock.Shared.MalaysiaNow.Date.AddDays(30);
        [StringLength(160)] public string? BannerText { get; set; }
        public string? BannerImageUrl { get; set; }
        public bool RemoveImage { get; set; }
        public bool ShowOnHome { get; set; }
        public bool IsActive { get; set; } = true;
        public List<int> DishIds { get; set; } = new();
    }

    public record DishRow(int Id, string Name, decimal Price, string? Image, string Category, int? PromotionId, string? PromotionTitle);

    public List<Promotion> Promotions { get; private set; } = new();
    public Promotion? Current { get; private set; }
    public bool IsNew => Input.Id == 0;
    public bool Editing => Current is not null || Mode == "new";
    public List<DishRow> Dishes { get; private set; } = new();
    public int HomeUsedByOthers { get; private set; }
    public DateTime Now { get; } = KOMA.Services.DemoClock.Shared.MalaysiaNow;

    private static readonly string[] Order = { "Live", "Today", "Scheduled", "Paused", "Ended" };
    public string StatusOf(Promotion p) => p.StatusAt(Now);
    public static string ColorOf(Promotion p, string status) => status is "Paused" or "Ended" ? "#c9c5ba" : Palette[p.Id % Palette.Length];
    public string StatusText(Promotion p) => StatusOf(p) switch
    {
        "Live" => "Live",
        "Today" => Promotion.Clock(p.StartTime!.Value),
        var s => s
    };

    public async Task OnGetAsync()
    {
        await LoadAsync();
        if (Current is not null) Input = ToInput(Current);
    }

    private async Task LoadAsync()
    {
        Promotions = (await _db.Promotions.Include(p => p.MenuItems).AsNoTracking().ToListAsync())
            .OrderBy(p => Array.IndexOf(Order, p.StatusAt(Now))).ThenBy(p => p.StartDate).ToList();
        if (Mode != "new")
            Current = Id is null ? Promotions.FirstOrDefault() : Promotions.FirstOrDefault(p => p.Id == Id);
        Dishes = await _db.MenuItems.AsNoTracking().OrderBy(m => m.MenuCategory!.Name).ThenBy(m => m.Name)
            .Select(m => new DishRow(m.Id, m.Name, m.Price, m.ImageUrl, m.MenuCategory!.Name, m.PromotionId, m.Promotion != null ? m.Promotion.Title : null))
            .ToListAsync();
        var editingId = Current?.Id ?? Input.Id;
        HomeUsedByOthers = Promotions.Count(p => p.Id != editingId && p.ShowOnHome && p.IsActive && p.EndDate.Date >= KOMA.Services.DemoClock.Shared.MalaysiaNow.Date);
    }

    private static PromoInput ToInput(Promotion p) => new()
    {
        Id = p.Id, Title = p.Title, DiscountPercent = p.DiscountPercent,
        Days = Enumerable.Range(0, 7).Where(p.RunsOn).ToList(),
        AllDay = p.AllDay,
        FromMin = p.StartTime is { } s ? s.Hour * 60 + s.Minute : 690,
        ToMin = p.EndTime is { } e ? e.Hour * 60 + e.Minute : 870,
        StartDate = p.StartDate.Date, EndDate = p.EndDate.Date,
        BannerText = p.BannerText, BannerImageUrl = p.BannerImageUrl, ShowOnHome = p.ShowOnHome, IsActive = p.IsActive,
        DishIds = p.MenuItems.Select(m => m.Id).ToList()
    };

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (Input.Days.Count == 0) ModelState.AddModelError("Input.Days", "Pick at least one day.");
        if (Input.EndDate.Date < Input.StartDate.Date) ModelState.AddModelError("Input.EndDate", "The end date can't be before the start date.");
        if (!Input.AllDay && Input.ToMin <= Input.FromMin) ModelState.AddModelError("Input.ToMin", "The end time must be after the start time.");
        if (Input.DishIds.Count == 0) ModelState.AddModelError("Input.DishIds", "Pick at least one dish.");
        if (Input.ShowOnHome)
        {
            var used = await _db.Promotions.CountAsync(p => p.Id != Input.Id && p.ShowOnHome && p.IsActive && p.EndDate >= KOMA.Services.DemoClock.Shared.MalaysiaNow.Date);
            if (used >= HomeSlots) ModelState.AddModelError("Input.ShowOnHome", $"All {HomeSlots} home page slots are in use. Turn one off first.");
        }

        Promotion promo;
        if (Input.Id == 0) { promo = new Promotion(); _db.Promotions.Add(promo); }
        else
        {
            promo = await _db.Promotions.Include(p => p.MenuItems).FirstOrDefaultAsync(p => p.Id == Input.Id) ?? null!;
            if (promo is null) return NotFound();
        }

        if (Banner is { Length: > 0 } && ModelState.IsValid)
        {
            try { promo.BannerImageUrl = await images.SaveAsync(Banner); }
            catch (ArgumentException ex) { ModelState.AddModelError(nameof(Banner), ex.Message); }
        }
        if (!ModelState.IsValid)
        {
            Input.BannerImageUrl = promo.BannerImageUrl;
            Id = Input.Id == 0 ? null : Input.Id; Mode = Input.Id == 0 ? "new" : null;
            await LoadAsync();
            if (Input.Id != 0) Current = Promotions.FirstOrDefault(p => p.Id == Input.Id);
            return Page();
        }

        promo.Title = Input.Title.Trim();
        promo.DiscountPercent = Input.DiscountPercent;
        promo.Days = Input.Days.Where(d => d is >= 0 and < 7).Aggregate(0, (m, d) => m | (1 << d));
        promo.StartTime = Input.AllDay ? null : new TimeOnly(Input.FromMin / 60, Input.FromMin % 60);
        promo.EndTime = Input.AllDay ? null : new TimeOnly(Input.ToMin / 60, Input.ToMin % 60);
        promo.StartDate = Input.StartDate.Date;
        promo.EndDate = Input.EndDate.Date;
        promo.BannerText = string.IsNullOrWhiteSpace(Input.BannerText) ? null : Input.BannerText.Trim();
        if (Input.RemoveImage && Banner is not { Length: > 0 }) promo.BannerImageUrl = null;
        promo.ShowOnHome = Input.ShowOnHome;
        promo.IsActive = Input.IsActive;

        // A dish belongs to one promotion at a time: picking it here moves it from any other promotion.
        var chosen = await _db.MenuItems.Where(m => Input.DishIds.Contains(m.Id)).ToListAsync();
        promo.MenuItems.Clear();
        foreach (var m in chosen) promo.MenuItems.Add(m);

        await _db.SaveChangesAsync();
        TempData["StatusMessage"] = Input.Id == 0 ? $"\"{promo.Title}\" created." : "Changes saved.";
        return RedirectToPage(new { id = promo.Id });
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var promo = await _db.Promotions.FindAsync(id);
        if (promo is null) return NotFound();
        promo.IsActive = !promo.IsActive;
        await _db.SaveChangesAsync();
        TempData["StatusMessage"] = promo.IsActive ? $"\"{promo.Title}\" resumed." : $"\"{promo.Title}\" paused.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var promo = await _db.Promotions.Include(p => p.MenuItems).FirstOrDefaultAsync(p => p.Id == id);
        if (promo is null) return NotFound();
        promo.MenuItems.Clear();                 // dishes stay on the menu, just without this discount
        _db.Promotions.Remove(promo);
        await _db.SaveChangesAsync();
        TempData["StatusMessage"] = $"\"{promo.Title}\" deleted.";
        return RedirectToPage(new { id = (int?)null });
    }
}

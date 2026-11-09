using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace KOMA.Pages;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public List<MenuItem> Dishes { get; private set; } = [];
    public List<MenuItem> RecommendedDishes { get; private set; } = [];
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
            .Where(i => i.IsAvailable && i.MenuCategory!.IsActive)
            .OrderBy(i => i.MenuCategory!.Name).ThenBy(i => i.Name)
            .ToListAsync();

        var availableIds = Dishes.Select(d => d.Id).ToArray();
        // Match the default Admin Reports period: today and the previous 29 days.
        var salesTo = KOMA.Services.DemoClock.Shared.MalaysiaNow.Date.AddDays(1);
        var salesFrom = salesTo.AddDays(-30);
        var sales = await db.OrderItems.AsNoTracking()
            .Where(i => i.Order!.Status != OrderStatus.Cancelled
                && i.Order.OrderedAt >= salesFrom && i.Order.OrderedAt < salesTo)
            .Select(i => new { i.MenuItemId, Name = i.ItemName != "" ? i.ItemName : i.MenuItem!.Name, i.Quantity, i.Order!.OrderedAt })
            .ToListAsync();
        var rankedDishes = DishPopularity.Rank(sales.Select(i => ((string?)i.Name, i.Quantity)));
        var bestSellerIds = new List<int>();
        foreach (var ranked in rankedDishes)
        {
            // Prefer the report's saved name: old orders may have missing or replaced menu IDs.
            var dish = Dishes.FirstOrDefault(d => string.Equals(d.Name.Trim(), ranked.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (dish == null)
            {
                var linkedIds = sales.Where(i => i.Name == ranked.Name && i.MenuItemId.HasValue)
                    .OrderByDescending(i => i.OrderedAt).Select(i => i.MenuItemId!.Value);
                dish = linkedIds.Select(id => Dishes.FirstOrDefault(d => d.Id == id)).FirstOrDefault(d => d != null);
            }
            if (dish != null && !bestSellerIds.Contains(dish.Id)) bestSellerIds.Add(dish.Id);
            if (bestSellerIds.Count == 3) break;
        }
        var selectedIds = new List<int>(bestSellerIds);
        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (User.IsInRole("Customer") && customerId != null)
        {
            var recentIds = await db.OrderItems.AsNoTracking()
                .Where(i => i.MenuItemId.HasValue && availableIds.Contains(i.MenuItemId.Value)
                    && !bestSellerIds.Contains(i.MenuItemId.Value)
                    && i.Order!.CustomerId == customerId
                    && i.Order.Status != OrderStatus.Cancelled
                    && i.Order.Status != OrderStatus.AwaitingArrival
                    && (i.Order.OrderType != OrderType.OnlinePreOrder || i.Order.Status != OrderStatus.Pending))
                .GroupBy(i => i.MenuItemId!.Value)
                .OrderByDescending(g => g.Max(i => i.Order!.OrderedAt)).ThenBy(g => g.Key)
                .Select(g => g.Key).Take(3).ToListAsync();
            selectedIds.AddRange(recentIds);
        }
        var adminPicks = Dishes.Where(d => d.IsRecommended && !selectedIds.Contains(d.Id)).Select(d => d.Id).ToArray();
        Random.Shared.Shuffle(adminPicks);
        selectedIds.AddRange(adminPicks.Take(10 - selectedIds.Count));
        var dishesById = Dishes.ToDictionary(d => d.Id);
        RecommendedDishes = selectedIds.Select(id => dishesById[id]).ToList();

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

using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Admin.MenuItems;

[Authorize(Roles = "Administrator")]
[RequestSizeLimit(6 * 1024 * 1024)]
public class EditModel(ApplicationDbContext _db, MenuImageService images) : PageModel
{
    [BindProperty] public MenuItem Item { get; set; } = new();
    [BindProperty] public IFormFile? Photo { get; set; }
    public SelectList CategoryOptions { get; set; } = null!;
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var existing = await _db.MenuItems.FindAsync(id); if (existing is null) return NotFound(); Item = existing;
        await LoadOptionsAsync();
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await _db.MenuItems.FindAsync(id); if (existing is null) return NotFound();
        if (!await _db.MenuCategories.AnyAsync(c => c.Id == Item.MenuCategoryId))
            ModelState.AddModelError("Item.MenuCategoryId", "Choose an existing category.");
        string? imageUrl = existing.ImageUrl;
        if (ModelState.IsValid && Photo is not null)
        {
            try { imageUrl = await images.SaveAsync(Photo); }
            catch (ArgumentException ex) { ModelState.AddModelError(nameof(Photo), ex.Message); }
        }
        if (!ModelState.IsValid)
        {
            Item.ImageUrl = existing.ImageUrl;
            await LoadOptionsAsync();
            return Page();
        }
        existing.Name = Item.Name;
        existing.Description = Item.Description;
        existing.Price = Item.Price;
        existing.MenuCategoryId = Item.MenuCategoryId;
        existing.IsAvailable = Item.IsAvailable;
        existing.IsRecommended = Item.IsRecommended;
        existing.IsSeasonal = Item.IsSeasonal;
        existing.ImageUrl = imageUrl;
        await _db.SaveChangesAsync();
        TempData["StatusMessage"] = "Menu item saved.";
        return RedirectToPage("./Index");
    }
    private async Task LoadOptionsAsync()
    {
        var categories = await _db.MenuCategories.OrderBy(c => c.DisplayOrder).ToListAsync();
        CategoryOptions = new SelectList(categories, nameof(MenuCategory.Id), nameof(MenuCategory.Name), Item.MenuCategoryId);
    }
}


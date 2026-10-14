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
public class CreateModel(ApplicationDbContext _db, MenuImageService images) : PageModel
{
    [BindProperty] public MenuItem Item { get; set; } = new();
    [BindProperty] public IFormFile? Photo { get; set; }
    public SelectList CategoryOptions { get; set; } = null!;
    public async Task<IActionResult> OnGetAsync(int id)
    {
        
        await LoadOptionsAsync();
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int id)
    {
        
        if (!await _db.MenuCategories.AnyAsync(c => c.Id == Item.MenuCategoryId))
            ModelState.AddModelError("Item.MenuCategoryId", "Choose an existing category.");
        string? imageUrl = null;
        if (ModelState.IsValid && Photo is not null)
        {
            try { imageUrl = await images.SaveAsync(Photo); }
            catch (ArgumentException ex) { ModelState.AddModelError(nameof(Photo), ex.Message); }
        }
        if (!ModelState.IsValid)
        {
            Item.ImageUrl = null;
            await LoadOptionsAsync();
            return Page();
        }
        _db.MenuItems.Add(new MenuItem {
                Name = Item.Name, Description = Item.Description, Price = Item.Price,
                MenuCategoryId = Item.MenuCategoryId, IsAvailable = Item.IsAvailable,
                IsRecommended = Item.IsRecommended, IsSeasonal = Item.IsSeasonal, ImageUrl = imageUrl
            });
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

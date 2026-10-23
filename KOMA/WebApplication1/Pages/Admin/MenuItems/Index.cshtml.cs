using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Admin.MenuItems;

// Admin "Menu" page (design C): categories on the left, the dishes in the chosen category in the middle,
// and a quick-edit panel on the right for a dish or a category. Replaces the separate Menu Items and
// Menu Categories list pages (the old Create/Edit/Delete pages still work).
[Authorize(Roles = "Administrator")]
[RequestSizeLimit(6 * 1024 * 1024)]
public class IndexModel(ApplicationDbContext _db, MenuImageService images) : PageModel
{
    // ---- Query string: /Admin/MenuItems?cat=3&id=12&q=roll&mode=new-item ----
    [BindProperty(SupportsGet = true)] public int? Cat { get; set; }        // selected category (null = all dishes)
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }         // selected dish
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }       // search text
    [BindProperty(SupportsGet = true)] public string? Mode { get; set; }    // item | new-item | edit-cat | new-cat

    // ---- Forms in the right-hand panel ----
    [BindProperty] public MenuItem Item { get; set; } = new();
    [BindProperty] public IFormFile? Photo { get; set; }
    [BindProperty] public MenuCategory Category { get; set; } = new();

    // ---- Page data ----
    public record CategoryRow(int Id, string Name, bool IsActive, int Count, int Unavailable);
    public List<CategoryRow> Categories { get; private set; } = new();
    public int AllCount { get; private set; }
    public MenuCategory? CurrentCategory { get; private set; }
    public List<MenuItem> Items { get; private set; } = new();
    public SelectList CategoryOptions { get; private set; } = null!;
    public string Panel { get; private set; } = "empty";                   // what the right panel shows

    public async Task OnGetAsync()
    {
        await LoadAsync();
        switch (Mode)
        {
            case "new-item":
                Item = new MenuItem { MenuCategoryId = Cat ?? Categories.FirstOrDefault()?.Id ?? 0, IsAvailable = true };
                Panel = "new-item";
                break;
            case "new-cat":
                Category = new MenuCategory();
                Panel = "new-cat";
                break;
            case "edit-cat" when CurrentCategory is not null:
                Category = CurrentCategory;
                Panel = "edit-cat";
                break;
            default:
                var selected = Id.HasValue
                    ? await _db.MenuItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == Id.Value)
                    : null;
                selected ??= Items.FirstOrDefault();
                if (selected is not null) { Item = selected; Id = selected.Id; Panel = "item"; }
                break;
        }
        SetCategoryOptions();
    }

    // ---------- Dish actions ----------

    // Create or update a dish (Item.Id == 0 means new).
    public async Task<IActionResult> OnPostSaveItemAsync()
    {
        KeepStateFor("Item.", nameof(Photo));
        var isNew = Item.Id == 0;
        var existing = isNew ? null : await _db.MenuItems.FindAsync(Item.Id);
        if (!isNew && existing is null) return NotFound();

        if (!await _db.MenuCategories.AnyAsync(c => c.Id == Item.MenuCategoryId))
            ModelState.AddModelError("Item.MenuCategoryId", "Choose a category.");

        var imageUrl = existing?.ImageUrl;
        if (ModelState.IsValid && Photo is not null)
        {
            try { imageUrl = await images.SaveAsync(Photo); }
            catch (ArgumentException ex) { ModelState.AddModelError(nameof(Photo), ex.Message); }
        }
        if (!ModelState.IsValid)
        {
            // Write the failing fields to the Output window (Debug) so they are easy to spot.
            foreach (var (key, entry) in ModelState.Where(e => e.Value!.Errors.Count > 0))
                System.Diagnostics.Debug.WriteLine($"[Menu save] {key}: {string.Join("; ", entry!.Errors.Select(er => er.ErrorMessage + er.Exception?.Message))}");
            Item.ImageUrl = existing?.ImageUrl;
            Id = isNew ? null : Item.Id;
            await LoadAsync();
            Panel = isNew ? "new-item" : "item";
            SetCategoryOptions();
            return Page();
        }

        existing ??= new MenuItem();
        existing.Name = Item.Name;
        existing.Description = Item.Description;
        existing.Price = Item.Price;
        existing.MenuCategoryId = Item.MenuCategoryId;
        existing.IsAvailable = Item.IsAvailable;
        existing.IsRecommended = Item.IsRecommended;
        existing.IsSeasonal = Item.IsSeasonal;
        existing.ImageUrl = imageUrl;
        if (isNew) _db.MenuItems.Add(existing);
        await _db.SaveChangesAsync();

        TempData["StatusMessage"] = isNew ? $"'{existing.Name}' was added." : $"'{existing.Name}' was saved.";
        // If the dish was moved to another category while a category was open, follow it there
        // so it stays visible (and selected) in the middle list.
        if (Cat.HasValue && Cat != existing.MenuCategoryId)
            return RedirectToPage("./Index", new RouteValueDictionary(Route(cat: existing.MenuCategoryId, id: existing.Id, keepSearch: false)));
        return Back(existing.Id);
    }

    // Quick on/off switch in the dish list.
    public async Task<IActionResult> OnPostToggleAsync(int itemId)
    {
        var item = await _db.MenuItems.FindAsync(itemId);
        if (item is null) return NotFound();
        item.IsAvailable = !item.IsAvailable;
        await _db.SaveChangesAsync();
        TempData["StatusMessage"] = $"'{item.Name}' is now {(item.IsAvailable ? "available" : "unavailable")}.";
        return Back(Id);
    }

    // Delete a dish from the menu. Past orders are not affected: each order line saved the dish name and
    // price when it was ordered (OrderItem.ItemName / UnitPrice), and its link to the dish is simply cleared.
    public async Task<IActionResult> OnPostDeleteItemAsync(int itemId)
    {
        var item = await _db.MenuItems.FindAsync(itemId);
        if (item is null) return Back(null);
        _db.MenuItems.Remove(item);
        await _db.SaveChangesAsync();
        TempData["StatusMessage"] = $"'{item.Name}' was deleted.";
        return Back(null);
    }

    // ---------- Category actions ----------

    public async Task<IActionResult> OnPostSaveCategoryAsync()
    {
        KeepStateFor("Category.");
        var isNew = Category.Id == 0;
        var existing = isNew ? null : await _db.MenuCategories.FindAsync(Category.Id);
        if (!isNew && existing is null) return NotFound();

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            Panel = isNew ? "new-cat" : "edit-cat";
            SetCategoryOptions();
            return Page();
        }

        existing ??= new MenuCategory();
        existing.Name = Category.Name;
        existing.Description = Category.Description;
        existing.IsActive = Category.IsActive;
        if (isNew) _db.MenuCategories.Add(existing);
        await _db.SaveChangesAsync();

        TempData["StatusMessage"] = isNew ? $"'{existing.Name}' was created." : $"'{existing.Name}' was updated.";
        return RedirectToPage("./Index", new RouteValueDictionary(Route(cat: existing.Id, keepSearch: false)));
    }

    // Delete a category (design A). If it still has dishes, the admin picks in the popup what happens to them:
    //   dishes = "move"   -> they move to the category chosen in moveTo (the default), or
    //   dishes = "delete" -> they are deleted too.
    // Past orders are safe either way: each order line keeps the dish name and price it was ordered with.
    public async Task<IActionResult> OnPostDeleteCategoryAsync(int categoryId, string? dishes, int? moveTo)
    {
        var category = await _db.MenuCategories.FindAsync(categoryId);
        if (category is null) return RedirectToPage("./Index");

        var items = await _db.MenuItems.Where(i => i.MenuCategoryId == category.Id).ToListAsync();
        var what = items.Count == 1 ? "1 dish" : $"{items.Count} dishes";
        var message = $"'{category.Name}' was deleted.";
        int? openAfter = null;

        if (items.Count > 0 && dishes == "delete")
        {
            _db.MenuItems.RemoveRange(items);
            message = $"'{category.Name}' and its {what} were deleted.";
        }
        else if (items.Count > 0)
        {
            var target = moveTo.HasValue && moveTo.Value != category.Id
                ? await _db.MenuCategories.FindAsync(moveTo.Value)
                : null;
            if (target is null)
            {
                TempData["StatusError"] = $"Choose a category to move the {what} in '{category.Name}' to, or choose to delete them.";
                return RedirectToPage("./Index", new RouteValueDictionary(Route(cat: category.Id, mode: "edit-cat", keepSearch: false)));
            }
            foreach (var item in items) item.MenuCategoryId = target.Id;
            message = $"'{category.Name}' was deleted. {what[0].ToString().ToUpper() + what[1..]} moved to '{target.Name}'.";
            openAfter = target.Id;    // show the category the dishes went to
        }

        _db.MenuCategories.Remove(category);
        await _db.SaveChangesAsync();   // one save: the dishes move (or go) and the category goes together
        TempData["StatusMessage"] = message;
        return openAfter.HasValue
            ? RedirectToPage("./Index", new RouteValueDictionary(Route(cat: openAfter, keepSearch: false)))
            : RedirectToPage("./Index");
    }

    // ---------- Helpers ----------

    private async Task LoadAsync()
    {
        Categories = await _db.MenuCategories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryRow(c.Id, c.Name, c.IsActive, c.Items.Count, c.Items.Count(i => !i.IsAvailable)))
            .ToListAsync();
        AllCount = Categories.Sum(c => c.Count);

        CurrentCategory = Cat.HasValue
            ? await _db.MenuCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == Cat.Value)
            : null;
        if (CurrentCategory is null) Cat = null;

        var query = _db.MenuItems.AsNoTracking().Include(i => i.MenuCategory).AsQueryable();
        if (Cat.HasValue) query = query.Where(i => i.MenuCategoryId == Cat.Value);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(i => i.Name.Contains(term) || (i.Description != null && i.Description.Contains(term)));
        }
        Items = await query.OrderByDescending(i => i.IsAvailable).ThenBy(i => i.Name).ToListAsync();
    }

    private void SetCategoryOptions() =>
        CategoryOptions = new SelectList(Categories, nameof(CategoryRow.Id), nameof(CategoryRow.Name), Item.MenuCategoryId);

    // The dish form and the category form share this page, so ASP.NET binds (and validates) both on every
    // post - and when one form's fields are missing it falls back to un-prefixed keys like "Name".
    // Keep only the errors that belong to the form that was actually submitted.
    private void KeepStateFor(params string[] keep)
    {
        foreach (var key in ModelState.Keys.ToList())
            if (!keep.Any(k => k.EndsWith('.') ? key.StartsWith(k, StringComparison.OrdinalIgnoreCase)
                                               : key.Equals(k, StringComparison.OrdinalIgnoreCase)))
                ModelState.Remove(key);
    }

    private IActionResult Back(int? id) =>
        RedirectToPage("./Index", new RouteValueDictionary(Route(id: id)));

    // Route values for links and forms that keep the current category and search. Pass a value to change it.
    private const int Keep = int.MinValue;
    public Dictionary<string, string> Route(int? cat = Keep, int? id = null, string? mode = null, bool keepSearch = true)
    {
        var r = new Dictionary<string, string>();
        var c = cat == Keep ? Cat : cat;
        if (c.HasValue) r["cat"] = c.Value.ToString();
        if (id.HasValue) r["id"] = id.Value.ToString();
        if (!string.IsNullOrEmpty(mode)) r["mode"] = mode;
        if (keepSearch && !string.IsNullOrWhiteSpace(Q)) r["q"] = Q!;
        return r;
    }

    // Soft colour for the photo placeholder, picked from the category so each category has its own tint.
    private static readonly string[] Tints = { "#f3dfda", "#f1e6d3", "#e4eedd", "#e3e9ee", "#eee7db", "#ece3ef" };
    public static string Tint(int categoryId) => Tints[Math.Abs(categoryId) % Tints.Length];
}

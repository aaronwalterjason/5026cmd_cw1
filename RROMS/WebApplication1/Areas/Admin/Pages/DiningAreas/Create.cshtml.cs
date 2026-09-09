using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Data;
using RROMS.Models.Admin;

namespace RROMS.Areas.Admin.Pages.DiningAreas
{
    [Authorize(Roles = "Administrator")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public CreateModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public DiningArea DiningArea { get; set; } = new();

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            _db.DiningAreas.Add(DiningArea);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"'{DiningArea.Name}' was created.";
            return RedirectToPage("./Index");
        }
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Pages.Admin.MenuCategories
{
    // Categories are now managed on the Menu page (/Admin/MenuItems) - left column + "Edit category" panel.
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        public IActionResult OnGet() => RedirectToPage("/Admin/MenuItems/Index");
    }
}

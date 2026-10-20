using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Pages.Admin.DiningAreas
{
    // Dining areas are now created by drawing them on a floor layout (Pages/Admin/Layouts/Designer),
    // so they get a position on the floor. This page just sends the admin there.
    [Authorize(Roles = "Administrator")]
    public class CreateModel : PageModel
    {
        public IActionResult OnGet()
        {
            TempData["StatusMessage"] = "Open a floor layout and use the Draw dining area tool to create a dining area.";
            return RedirectToPage("/Admin/Layouts/Index");
        }

        public IActionResult OnPost() => OnGet();
    }
}

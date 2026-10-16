using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Pages.Staff
{
    [Authorize(Roles = "Staff")]
    public class IndexModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
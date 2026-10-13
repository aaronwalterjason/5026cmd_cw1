using Microsoft.AspNetCore.Identity;

namespace KOMA.Models
{
    // Extend Identity's built-in user with any KOMA-specific profile fields as they come up
    // (e.g. a display name for booking confirmations).
    public class ApplicationUser : IdentityUser
    {
        public string? FullName { get; set; }
        public Customer? Customer { get; set; }
        public Staff? Staff { get; set; }
        public Administrator? Administrator { get; set; }
    }
}

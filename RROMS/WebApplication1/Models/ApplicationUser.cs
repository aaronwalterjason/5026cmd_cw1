using Microsoft.AspNetCore.Identity;

namespace RROMS.Models
{
    // Extend Identity's built-in user with any RROMS-specific profile fields as they come up
    // (e.g. a display name for booking confirmations).
    public class ApplicationUser : IdentityUser
    {
        public string? FullName { get; set; }
    }
}

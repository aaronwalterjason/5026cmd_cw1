using Microsoft.AspNetCore.Identity;

namespace KOMA.Models
{
    // Extend Identity's built-in user with any KOMA-specific profile fields as they come up
    // (e.g. a display name for booking confirmations).
    public class ApplicationUser : IdentityUser
    {
        public string? FullName { get; set; }

        // Customer turned off booking reminder emails (Pages/Notifications/Reminders). Other booking emails always send.
        public bool NoReminderEmails { get; set; }
        [PersonalData]
        [System.ComponentModel.DataAnnotations.StringLength(50)]
        public string? FirstName { get; set; }
        [PersonalData]
        [System.ComponentModel.DataAnnotations.StringLength(49)]
        public string? LastName { get; set; }
        public Customer? Customer { get; set; }
        public Staff? Staff { get; set; }
        public Administrator? Administrator { get; set; }
    }
}

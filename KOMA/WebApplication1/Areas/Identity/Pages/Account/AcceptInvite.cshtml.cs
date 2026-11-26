using System.ComponentModel.DataAnnotations;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Areas.Identity.Pages.Account;

// Complete registration: opened from the staff/admin invitation email (Admin > Accounts > Invite staff / Invite admin).
// The admin only entered the email. Here the person fills in the rest of their AspNetUsers row:
// FirstName, LastName (FullName is built from them), PhoneNumber and a password. The email can't be changed.
// Opening the link proves they own the inbox, so the email is confirmed too.
// The link stops working once used (setting the password renews the security stamp) or after 24 hours.
[AllowAnonymous]
public class AcceptInviteModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    AccountEmailService emails, ApplicationDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Code { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public bool LinkOk { get; private set; }
    public string Email { get; private set; } = "";
    public string RoleLabel { get; private set; } = "Staff";

    // Same limits as the AspNetUsers columns (ApplicationUser.FirstName 50, LastName 49).
    public class InputModel
    {
        [Required(ErrorMessage = "Enter your first name."), StringLength(50), Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;
        [Required(ErrorMessage = "Enter your last name."), StringLength(49), Display(Name = "Last name")]
        public string LastName { get; set; } = string.Empty;
        [Required(ErrorMessage = "Enter your phone number."), RegularExpression(PhoneRules.Pattern, ErrorMessage = PhoneRules.Message), StringLength(30), Display(Name = "Phone number")]
        public string PhoneNumber { get; set; } = string.Empty;
        [Required(ErrorMessage = "Choose a password."), StringLength(100, MinimumLength = 6, ErrorMessage = "The password must be at least 6 characters."),
         DataType(DataType.Password), Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;
        [Required(ErrorMessage = "Type the password again."), Compare(nameof(Password), ErrorMessage = "The passwords don't match."),
         DataType(DataType.Password), Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task OnGetAsync() => await CheckLinkAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var (user, role) = await CheckLinkAsync();
        if (user is null || role is null) return Page();   // shows the "expired" message
        if (!ModelState.IsValid) return Page();

        var first = Input.FirstName.Trim();
        var last = Input.LastName.Trim();
        user.FirstName = first;
        user.LastName = last;
        var full = $"{first} {last}";
        user.FullName = full;
        user.PhoneNumber = Input.PhoneNumber.Trim();
        user.EmailConfirmed = true;
        // AddPasswordAsync checks the password rules first, then saves the whole row (names, phone, confirmed email)
        // in one update and renews the security stamp, so this link can't be used again.
        var result = await users.AddPasswordAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }

        db.ActivityLogs.Add(new ActivityLog
        {
            UserId = user.Id, UserRole = role == "Administrator" ? "Admin" : "Staff", UserName = full,
            Area = ActivityLog.Areas.Settings, Action = "Invite accepted",
            Title = full, Summary = $"{RoleLabel} registration completed · {user.Email}"
        });
        await db.SaveChangesAsync();

        await signIn.SignInAsync(user, isPersistent: false);
        TempData["StatusTitle"] = "Registration complete";
        TempData["StatusMessage"] = $"Welcome to the KOMA team, {first}.";
        return role == "Administrator"
            ? RedirectToPage("/Admin/Dashboard", new { area = "" })
            : RedirectToPage("/Staff/Index", new { area = "" });
    }

    // The link is good when: the account exists, is a Staff/Admin account, has no password yet (never completed)
    // and the code is valid for it (not expired, not replaced by a newer invite).
    private async Task<(ApplicationUser? User, string? Role)> CheckLinkAsync()
    {
        LinkOk = false;
        if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Code)) return (null, null);
        var user = await users.FindByIdAsync(UserId);
        if (user is null || await users.HasPasswordAsync(user)) return (null, null);
        var role = (await users.GetRolesAsync(user)).FirstOrDefault(r => r is "Staff" or "Administrator");
        if (role is null || !await emails.IsInviteCodeValidAsync(user, Code)) return (null, null);
        LinkOk = true;
        Email = user.Email ?? "";
        RoleLabel = role == "Administrator" ? "Admin" : "Staff";
        return (user, role);
    }
}

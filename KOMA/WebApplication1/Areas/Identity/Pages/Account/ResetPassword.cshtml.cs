using System.ComponentModel.DataAnnotations;
using KOMA.Models;
using KOMA.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Areas.Identity.Pages.Account;

// Reset password: opened from the reset email (or the admin's "Password reset link" for staff).
// Using the link proves the person owns the inbox, so a successful reset also confirms the email.
[AllowAnonymous]
public class ResetPasswordModel(UserManager<ApplicationUser> users) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Code { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool Done { get; private set; }

    public class InputModel
    {
        [Required, EmailAddress, Display(Name = "Email address")] public string Email { get; set; } = "";
        [Required, StringLength(100, MinimumLength = 6), DataType(DataType.Password), Display(Name = "New password")] public string Password { get; set; } = "";
        [Required, Compare(nameof(Password), ErrorMessage = "The passwords don't match."), DataType(DataType.Password), Display(Name = "Confirm new password")] public string ConfirmPassword { get; set; } = "";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var token = AccountEmailService.Decode(Code);
        if (token is null) ModelState.AddModelError(string.Empty, "This reset link isn't valid. Ask for a new one.");
        if (!ModelState.IsValid) return Page();

        var user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user is null) { Done = true; return Page(); }   // same result as success: don't reveal accounts
        var result = await users.ResetPasswordAsync(user, token!, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(string.Empty, e.Code == "InvalidToken" ? "This reset link has expired or was already used. Ask for a new one." : e.Description);
            return Page();
        }
        if (!user.EmailConfirmed) { user.EmailConfirmed = true; await users.UpdateAsync(user); }
        await users.ResetAccessFailedCountAsync(user);
        Done = true;
        return Page();
    }
}

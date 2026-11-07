using System.ComponentModel.DataAnnotations;
using KOMA.Models;
using KOMA.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Areas.Identity.Pages.Account;

// Forgot password: emails a KOMA-branded reset link. The page always shows the same message,
// so it never reveals whether an email address has an account.
[AllowAnonymous]
public class ForgotPasswordModel(UserManager<ApplicationUser> users, AccountEmailService emails) : PageModel
{
    [BindProperty, Required, EmailAddress, Display(Name = "Email address")]
    public string Email { get; set; } = "";
    public bool Done { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await users.FindByEmailAsync(Email.Trim());
        if (user is not null)
            await emails.SendResetAsync(user, code =>
                Url.Page("/Account/ResetPassword", null, new { area = "Identity", code }, Request.Scheme)!);
        Done = true;
        return Page();
    }
}

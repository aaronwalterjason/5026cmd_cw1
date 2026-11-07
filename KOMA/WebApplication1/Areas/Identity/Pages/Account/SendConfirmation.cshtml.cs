using KOMA.Models;
using KOMA.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Areas.Identity.Pages.Account;

// "Resend email" on the confirm-your-email banner (Pages/Shared/_ConfirmEmailBanner.cshtml). POST only.
[Authorize]
public class SendConfirmationModel(UserManager<ApplicationUser> users, AccountEmailService emails) : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/");

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        var user = await users.GetUserAsync(User);
        if (user is not null && !user.EmailConfirmed)
        {
            var sent = await emails.SendConfirmAsync(user, code =>
                Url.Page("/Account/ConfirmEmail", null, new { area = "Identity", userId = user.Id, code }, Request.Scheme)!);
            TempData["ConfirmBanner"] = sent ? $"We've sent a new link to {user.Email}." : "A link was sent a moment ago — please check your inbox and spam folder.";
        }
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}

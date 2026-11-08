using KOMA.Models;
using KOMA.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Areas.Identity.Pages.Account;

// Link in the "Confirm your email" email: /Identity/Account/ConfirmEmail?userId=...&code=...
[AllowAnonymous]
public class ConfirmEmailModel(UserManager<ApplicationUser> users) : PageModel
{
    public bool Ok { get; private set; }

    public async Task OnGetAsync(string? userId, string? code)
    {
        var token = AccountEmailService.Decode(code);
        var user = userId is null ? null : await users.FindByIdAsync(userId);
        if (user is null || token is null) return;
        Ok = user.EmailConfirmed || (await users.ConfirmEmailAsync(user, token)).Succeeded;
    }
}

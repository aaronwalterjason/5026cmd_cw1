using KOMA.Models;
using KOMA.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Pages.Notifications;

// "Turn off reminders" link in reminder emails. The link carries a signed token (not the user id),
// so nobody can switch off someone else's reminders by guessing. Booking emails (confirmed / updated /
// cancelled) always send; only reminders can be turned off.
[AllowAnonymous]
public class RemindersModel(IDataProtectionProvider dp, UserManager<ApplicationUser> users) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? T { get; set; }
    public string? Email { get; private set; }
    public bool Off { get; private set; }
    public bool Invalid { get; private set; }

    private async Task<ApplicationUser?> UserFromTokenAsync()
    {
        if (string.IsNullOrEmpty(T)) return null;
        try { return await users.FindByIdAsync(dp.CreateProtector(BookingEmailWorker.RemindersPurpose).Unprotect(T)); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    public async Task OnGetAsync()
    {
        var u = await UserFromTokenAsync();
        if (u is null) { Invalid = true; return; }
        Email = u.Email; Off = u.NoReminderEmails;
    }

    public async Task<IActionResult> OnPostAsync(bool off)
    {
        var u = await UserFromTokenAsync();
        if (u is null) { Invalid = true; return Page(); }
        u.NoReminderEmails = off;
        await users.UpdateAsync(u);
        Email = u.Email; Off = off;
        return Page();
    }
}

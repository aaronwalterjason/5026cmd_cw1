using KOMA.Data;
using KOMA.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace KOMA.Services;

// The normal Identity sign-in, plus an Activity log entry ("Signed in" / "Failed sign-in") for admin and staff
// accounts. Customers' sign-ins are not logged. Logging can never stop someone signing in.
public class AuditSignInManager(
    UserManager<ApplicationUser> userManager, IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory, IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger, IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation, ApplicationDbContext db)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<SignInResult> PasswordSignInAsync(string userName, string password, bool isPersistent, bool lockoutOnFailure)
    {
        var result = await base.PasswordSignInAsync(userName, password, isPersistent, lockoutOnFailure);
        try
        {
            var user = await UserManager.FindByNameAsync(userName) ?? await UserManager.FindByEmailAsync(userName);
            if (user is not null)
            {
                var roles = await UserManager.GetRolesAsync(user);
                var role = roles.Contains("Administrator") ? "Admin" : roles.Contains("Staff") ? "Staff" : null;
                if (role is not null)
                {
                    db.ActivityLogs.Add(new ActivityLog
                    {
                        At = DateTime.Now, UserId = user.Id, UserRole = role,
                        UserName = user.FullName ?? user.Email ?? userName,
                        Area = ActivityLog.Areas.SignIns,
                        Action = result.Succeeded ? "Signed in" : result.IsLockedOut ? "Locked out" : "Failed sign-in",
                        Title = user.Email ?? userName,
                        Summary = Device()
                    });
                    await db.SaveChangesAsync();
                }
            }
        }
        catch { /* never block a sign-in because the log could not be written */ }
        return result;
    }

    // "Chrome · Windows · 10.0.0.10" from the browser's user-agent and the network address.
    private string Device()
    {
        var ctx = Context;
        var ua = ctx.Request.Headers.UserAgent.ToString();
        var browser = ua.Contains("Edg/") ? "Edge" : ua.Contains("Chrome/") ? "Chrome" : ua.Contains("Firefox/") ? "Firefox" : ua.Contains("Safari/") ? "Safari" : "Browser";
        var os = ua.Contains("Android") ? "Android" : ua.Contains("iPhone") || ua.Contains("iPad") ? "iOS" : ua.Contains("Windows") ? "Windows" : ua.Contains("Mac OS") ? "Mac" : "Unknown device";
        var ip = ctx.Connection.RemoteIpAddress?.ToString();
        return $"{browser} · {os}" + (string.IsNullOrEmpty(ip) ? "" : $" · {ip}");
    }
}

using System.Text;
using KOMA.Data;
using KOMA.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KOMA.Services.Email;

// Account emails: "Confirm your email" (after sign-up) and "Reset your password" (Forgot password).
// Sent straight away (the customer is waiting for them) and listed on Admin > Activity > Emails.
// The links carry one-time tokens, so the email body is NOT saved and these can't be resent from the log.
public class AccountEmailService(
    UserManager<ApplicationUser> users, IMailSender sender, ApplicationDbContext db,
    IOptionsMonitor<EmailOptions> options, ILogger<AccountEmailService> log)
{
    private static readonly TimeSpan MinGap = TimeSpan.FromMinutes(1);   // one email per minute per address and type

    public static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    public static string? Decode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); } catch (FormatException) { return null; }
    }

    // linkFor(code) builds the page address with the encoded token.
    public async Task<bool> SendConfirmAsync(ApplicationUser user, Func<string, string> linkFor)
    {
        if (user.EmailConfirmed || string.IsNullOrEmpty(user.Email)) return false;
        var code = Encode(await users.GenerateEmailConfirmationTokenAsync(user));
        return await SendAsync(user, BookingEmailType.ConfirmEmail, linkFor(code));
    }

    public async Task<bool> SendResetAsync(ApplicationUser user, Func<string, string> linkFor)
    {
        if (string.IsNullOrEmpty(user.Email)) return false;
        var code = Encode(await users.GeneratePasswordResetTokenAsync(user));
        return await SendAsync(user, BookingEmailType.PasswordReset, linkFor(code));
    }

    // Staff / admin invitation: the code for the "Complete registration" link (valid 24 hours, see InviteTokenProvider).
    public async Task<string> InviteCodeAsync(ApplicationUser user) =>
        Encode(await users.GenerateUserTokenAsync(user, KOMA.Services.InviteTokenProvider.ProviderName, KOMA.Services.InviteTokenProvider.Purpose));

    public async Task<bool> IsInviteCodeValidAsync(ApplicationUser user, string? code) =>
        Decode(code) is { } token &&
        await users.VerifyUserTokenAsync(user, KOMA.Services.InviteTokenProvider.ProviderName, KOMA.Services.InviteTokenProvider.Purpose, token);

    // linkFor(code) builds the AcceptInvite address. role: "Staff" or "Administrator".
    public async Task<bool> SendInviteAsync(ApplicationUser user, string role, string invitedBy, Func<string, string> linkFor)
    {
        if (string.IsNullOrEmpty(user.Email)) return false;
        var link = linkFor(await InviteCodeAsync(user));
        var built = BookingEmailTemplates.BuildInvite(role == "Administrator" ? "Admin" : "Staff", invitedBy, link, options.CurrentValue);
        return await SendAsync(user, BookingEmailType.StaffInvite, built);
    }

    private Task<bool> SendAsync(ApplicationUser user, BookingEmailType type, string link)
    {
        var name = !string.IsNullOrWhiteSpace(user.FirstName) ? user.FirstName! : (user.FullName ?? user.Email!).Split(' ')[0];
        return SendAsync(user, type, BookingEmailTemplates.BuildAccount(type, name, link, options.CurrentValue));
    }

    private async Task<bool> SendAsync(ApplicationUser user, BookingEmailType type, BookingEmailTemplates.Built built)
    {
        var now = ReservationRules.MalaysiaNow;
        var since = now - MinGap;
        if (await db.BookingEmails.AnyAsync(e => e.Type == type && e.ToEmail == user.Email && e.CreatedAt >= since)) return false;

        var name = user.FullName ?? user.Email!.Split('@')[0];
        var row = new BookingEmail
        {
            Type = type, ToEmail = user.Email, ToName = name, CreatedAt = now, SendAfter = now,
            Subject = built.Subject, Attempts = 1
        };
        try
        {
            await sender.SendAsync(user.Email!, user.FullName, built.Subject, built.Html, built.Text);
            row.Status = BookingEmailStatus.Sent; row.SentAt = ReservationRules.MalaysiaNow;
        }
        catch (Exception ex)
        {
            row.Status = BookingEmailStatus.Failed;
            row.Note = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
            log.LogWarning(ex, "Account email ({Type}) to {Email} failed", type, user.Email);
        }
        try { db.BookingEmails.Add(row); await db.SaveChangesAsync(); } catch (DbUpdateException) { /* the log is best-effort */ }
        return row.Status == BookingEmailStatus.Sent;
    }
}

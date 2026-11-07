using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using MimeKit;

namespace KOMA.Services.Email;

public record MailAttachment(string FileName, string ContentType, byte[] Data);

public interface IMailSender
{
    Task SendAsync(string to, string? toName, string subject, string html, string text,
        IEnumerable<MailAttachment>? attachments = null, CancellationToken ct = default);
}

// Sends through SMTP with MailKit (smtp4dev while developing, Gmail / Brevo for real).
// Also used by Identity (confirm email, reset password) through IEmailSender.
public class MailKitSender(IOptionsMonitor<EmailOptions> options, IWebHostEnvironment env) : IMailSender, IEmailSender
{
    public async Task SendAsync(string to, string? toName, string subject, string html, string text,
        IEnumerable<MailAttachment>? attachments = null, CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(o.FromName, o.From));
        msg.To.Add(new MailboxAddress(toName ?? "", to));
        if (!string.IsNullOrWhiteSpace(o.ReplyTo)) msg.ReplyTo.Add(MailboxAddress.Parse(o.ReplyTo));
        msg.Subject = subject;
        var body = new BodyBuilder { HtmlBody = html, TextBody = text };
        foreach (var a in attachments ?? [])
            body.Attachments.Add(a.FileName, a.Data, ContentType.Parse(a.ContentType));
        msg.Body = body.ToMessageBody();

        if (string.IsNullOrWhiteSpace(o.Host))
        {
            // No mail server configured: save the email so it can still be opened and checked.
            var folder = Path.Combine(env.ContentRootPath, o.PickupFolder);
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml");
            await msg.WriteToAsync(file, ct);
            return;
        }

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(o.Host, o.Port, SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrWhiteSpace(o.User)) await smtp.AuthenticateAsync(o.User, o.Password ?? "", ct);
        await smtp.SendAsync(msg, ct);
        await smtp.DisconnectAsync(true, ct);
    }

    // Identity UI emails (account confirmation, password reset)
    public Task SendEmailAsync(string email, string subject, string htmlMessage) =>
        SendAsync(email, null, subject, htmlMessage, System.Text.RegularExpressions.Regex.Replace(htmlMessage, "<[^>]+>", ""));
}

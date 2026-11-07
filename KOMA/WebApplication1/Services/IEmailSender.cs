namespace KOMA.Services;

public interface IEmailSender
{
    Task SendEmailAsync(string email, string subject, string htmlMessage);
}

// Preserve the previous Identity UI placeholder until an email service is configured.
public sealed class UnconfiguredEmailSender : IEmailSender
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
}
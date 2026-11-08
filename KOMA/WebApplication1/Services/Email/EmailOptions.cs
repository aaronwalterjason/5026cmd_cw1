namespace KOMA.Services.Email;

// appsettings.json "Email" section. Keep the password out of appsettings: use User Secrets
//   dotnet user-secrets set "Email:Password" "your-app-password"
// Host empty = no mail server: emails are saved as .eml files in PickupFolder (open them with Outlook / Mail).
public class EmailOptions
{
    public string? Host { get; set; }                 // smtp4dev: localhost · Gmail: smtp.gmail.com
    public int Port { get; set; } = 25;               // smtp4dev: 25 · Gmail: 587
    public string? User { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@koma.my";
    public string FromName { get; set; } = "KOMA";
    public string? ReplyTo { get; set; } = "hello@koma.my";
    public string SiteUrl { get; set; } = "https://localhost:7275";
    public string PickupFolder { get; set; } = "App_Data/mail";
    public string Address { get; set; } = "Lot 3, Jalan Kiara 2, 50480 Kuala Lumpur";
    public string Phone { get; set; } = "03-6201 4455";
    public int ReminderHours { get; set; } = 24;      // 0 = no reminders
}

using System.Net;
using System.Text;
using System.Text.Json;
using KOMA.Models;

namespace KOMA.Services.Email;

// Builds each booking email from the booking as it is now (nothing here is fixed text about a booking).
// HTML uses tables and inline styles because mail apps (Outlook, Gmail) ignore most CSS; a plain-text copy goes with it.
public static class BookingEmailTemplates
{
    public record Built(string Subject, string Html, string Text, List<MailAttachment> Attachments);

    private const string Navy = "#263b49", Rust = "#974535", Paper = "#f4f1e9", Ink = "#22343d", Muted = "#6f7a76", Line = "#ebe6db";
    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
    public static string Day(DateTime d) => d.ToString("dddd, d MMM yyyy");
    public static string Time(DateTime d) => d.ToString("h:mm tt").ToLowerInvariant();
    public static string Seating(Reservation r) => r.RestaurantTable is null ? "" :
        (r.RestaurantTable.DiningArea is null ? "" : r.RestaurantTable.DiningArea.Name + " · ") + "Table " + r.RestaurantTable.TableNumber;
    public static string Ref(Reservation r) => $"KOMA-B-{r.Id:D6}";

    public static Built Build(BookingEmail e, Reservation r, Order? preOrder, EmailOptions o, string? remindersOffUrl)
    {
        var guest = r.CustomerUserId is null;   // no account: no sign-in links
        var view = $"{o.SiteUrl.TrimEnd('/')}/Reservations/Details/{r.Id}";
        var book = $"{o.SiteUrl.TrimEnd('/')}/Reservations/Edit";
        var first = (r.CustomerName ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
        var details = new List<(string, string)>
        {
            ("Date", Day(r.StartTime)), ("Booking time", Time(r.StartTime)),
            ("Guests", r.GuestCount.ToString()), ("Seating", Seating(r)), ("Booking ref", Ref(r))
        };
        string subject, kana, title, intro, body;
        var buttons = new List<(string Text, string Url, bool Solid)>();
        var atts = new List<MailAttachment>();
        var text = new StringBuilder();

        switch (e.Type)
        {
            case BookingEmailType.Confirmed:
                subject = "Your KOMA booking is confirmed"; kana = "席"; title = "Your table is booked";
                intro = $"Hi {H(first)}, thank you for booking with KOMA. Here are your details — we look forward to seeing you.";
                body = Details(details) + PreOrderBox(preOrder, r);
                if (!guest) { buttons.Add(("View booking", view, true)); buttons.Add(("Change or cancel", view, false)); }
                atts.Add(Ics(r, o, 0));
                break;

            case BookingEmailType.Updated:
                subject = "Your KOMA booking has been updated"; kana = "変"; title = "Your booking was updated";
                intro = e.By == "staff"
                    ? $"Hi {H(first)}, our team updated your booking. Please check the new details below. If they don't suit you, you can change or cancel it."
                    : $"Hi {H(first)}, your changes are saved. Here is your booking as it is now.";
                var changes = e.ChangesJson is null ? new() : JsonSerializer.Deserialize<Dictionary<string, string[]>>(e.ChangesJson) ?? new();
                body = Label("What changed") + ChangeTable(changes) + Label("Your booking now") + Details(details)
                       + (changes.ContainsKey("Pre-order") ? PreOrderBox(preOrder, r) : "");
                if (!guest) { buttons.Add(("View booking", view, true)); buttons.Add(("Change or cancel", view, false)); }
                atts.Add(Ics(r, o, 1));
                break;

            case BookingEmailType.Cancelled:
                subject = "Your KOMA booking has been cancelled"; kana = "止"; title = "Your booking was cancelled";
                intro = $"Hi {H(first)}, this booking has been cancelled and the table is no longer held for you.";
                var who = e.By == "customer" ? "You cancelled this booking." : "This booking was cancelled by KOMA. We're sorry for any trouble — please call us if you have questions.";
                body = Details(details.Where(d => d.Item1 != "Seating").ToList())
                       + $"<div style=\"margin-top:16px;border-left:3px solid {Rust};background:#faf3f0;padding:10px 14px;font-size:13px;line-height:1.55\">{who}</div>"
                       + (preOrder is not null ? $"<p style=\"margin:12px 0 0;font-size:12.5px;line-height:1.55;color:#3b4a52\">Your pre-order (RM {preOrder.TotalAmount:0.00}) has been cancelled too.</p>" : "");
                buttons.Add(("Book another time", book, true));
                break;

            default: // Reminder
                subject = "See you soon at KOMA"; kana = "明";
                var tomorrow = r.StartTime.Date == ReservationRules.MalaysiaNow.Date.AddDays(1);
                title = tomorrow ? "See you tomorrow" : "See you soon";
                intro = $"Hi {H(first)}, a quick reminder of your table at KOMA.";
                body = Details([(tomorrow ? "Tomorrow" : "When", $"{Day(r.StartTime)} · {Time(r.StartTime)}"), ("Guests", r.GuestCount.ToString()), ("Seating", Seating(r)), ("Booking ref", Ref(r))])
                       + Tips(o, preOrder is not null);
                if (!guest) { buttons.Add(("View booking", view, true)); buttons.Add(("Can't make it? Change or cancel", view, false)); }
                buttons.Add(("Open in Maps", "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(o.Address), false));
                break;
        }

        var html = Page(kana, title, intro, body, buttons, o, e.Type == BookingEmailType.Reminder ? remindersOffUrl : null);

        // plain-text copy
        text.AppendLine(title).AppendLine().AppendLine(WebUtility.HtmlDecode(intro)).AppendLine();
        if (e.Type == BookingEmailType.Updated && e.ChangesJson is not null)
        {
            foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, string[]>>(e.ChangesJson) ?? new())
                text.AppendLine($"{k}: {v[0]} -> {v[1]}");
            text.AppendLine();
        }
        foreach (var (k, v) in details) text.AppendLine($"{k}: {v}");
        text.AppendLine();
        foreach (var b in buttons) text.AppendLine($"{b.Text}: {b.Url}");
        text.AppendLine().AppendLine($"KOMA · {o.Address} · {o.Phone}");
        if (e.Type == BookingEmailType.Reminder && remindersOffUrl is not null) text.AppendLine($"Turn off reminders: {remindersOffUrl}");

        return new Built(subject, html, text.ToString(), atts);
    }

    // ---------- account emails (Confirm email, Password reset) ----------
    public static Built BuildAccount(BookingEmailType type, string firstName, string link, EmailOptions o)
    {
        var confirm = type == BookingEmailType.ConfirmEmail;
        var subject = confirm ? "Confirm your email for KOMA" : "Reset your KOMA password";
        var title = confirm ? "Confirm your email" : "Reset your password";
        var intro = confirm
            ? $"Hi {H(firstName)}, welcome to KOMA. Please confirm this is your email address so we can send you booking updates and help you if you forget your password."
            : $"Hi {H(firstName)}, we received a request to reset the password for your KOMA account. Choose a new password using the button below.";
        var note = confirm
            ? "If you didn't create a KOMA account, you can ignore this email."
            : "This link works once and expires after a short time. If you didn't ask to reset your password, you can ignore this email — your password won't change.";
        var button = $"<a href=\"{H(link)}\" style=\"display:inline-block;padding:12px 26px;border-radius:99px;background:{Navy};color:#f4f1e9;font-size:13.5px;font-weight:700;text-decoration:none\">{(confirm ? "Confirm email" : "Choose a new password")}</a>";
        var body = $"<div style=\"margin:4px 0 20px\">{button}</div>"
                 + $"<p style=\"margin:0;font-size:12.5px;line-height:1.55;color:{Muted}\">{H(note)}</p>"
                 + $"<p style=\"margin:14px 0 0;font-size:11.5px;line-height:1.5;color:{Muted};word-break:break-all\">If the button doesn't work, copy this link into your browser:<br>{H(link)}</p>";
        var html = Page(confirm ? "確" : "鍵", title, intro, body, [], o, null);
        var text = $"{title}\n\n{WebUtility.HtmlDecode(intro)}\n\n{(confirm ? "Confirm email" : "Choose a new password")}: {link}\n\n{note}\n\nKOMA · {o.Address} · {o.Phone}\n";
        return new Built(subject, html, text, []);
    }

    // ---------- pieces ----------
    private static string Label(string t) =>
        $"<div style=\"font-size:11px;font-weight:700;letter-spacing:.1em;color:{Muted};margin:16px 0 4px\">{H(t.ToUpperInvariant())}</div>";

    private static string Details(List<(string K, string V)> rows)
    {
        var sb = new StringBuilder("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse\">");
        foreach (var (k, v) in rows.Where(r => !string.IsNullOrEmpty(r.V)))
            sb.Append($"<tr><td style=\"padding:9px 0;color:{Muted};font-size:12.5px;width:120px;border-bottom:1px solid {Line}\">{H(k)}</td>")
              .Append($"<td style=\"padding:9px 0;font-size:13.5px;font-weight:700;color:{Ink};border-bottom:1px solid {Line}\">{H(v)}</td></tr>");
        return sb.Append("</table>").ToString();
    }

    private static string ChangeTable(Dictionary<string, string[]> changes)
    {
        var sb = new StringBuilder("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse\">");
        foreach (var (k, v) in changes)
            sb.Append($"<tr><td style=\"padding:9px 0;color:{Muted};font-size:12.5px;width:100px;border-bottom:1px solid {Line}\">{H(k)}</td>")
              .Append($"<td style=\"padding:9px 0;font-size:12.5px;color:{Muted};text-decoration:line-through;border-bottom:1px solid {Line}\">{H(v[0])}</td>")
              .Append($"<td style=\"padding:9px 6px;color:{Muted};border-bottom:1px solid {Line}\">&rarr;</td>")
              .Append($"<td style=\"padding:9px 6px;font-size:13.5px;font-weight:700;color:{Ink};background:#f6f1e4;border-bottom:1px solid {Line}\">{H(v[1])}</td></tr>");
        return sb.Append("</table>").ToString();
    }

    private static string PreOrderBox(Order? o, Reservation r)
    {
        if (o is null || o.Items.Count == 0) return "";
        var sb = new StringBuilder($"<div style=\"margin-top:18px;background:{Paper};border-radius:12px;padding:14px 16px\">{Label("Your pre-order").Replace("margin:16px 0 4px", "margin:0 0 8px")}")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">");
        foreach (var i in o.Items)
            sb.Append($"<tr><td style=\"padding:3px 0;width:30px;color:{Muted};font-size:13px\">{i.Quantity}×</td><td style=\"font-size:13px\">{H(i.ItemName)}</td>")
              .Append($"<td style=\"text-align:right;font-size:13px\">RM {i.UnitPrice * i.Quantity:0.00}</td></tr>");
        sb.Append($"<tr><td colspan=\"2\" style=\"padding-top:8px;border-top:1px solid #e3dccd;font-weight:700;font-size:13px\">Total</td>")
          .Append($"<td style=\"padding-top:8px;border-top:1px solid #e3dccd;text-align:right;font-weight:700;font-size:13px\">RM {o.TotalAmount:0.00}</td></tr></table>")
          .Append($"<div style=\"font-size:11.5px;color:{Muted};margin-top:6px\">You can change it until {Time(r.StartTime)} on {r.StartTime:dddd}. It goes to the kitchen when you scan the QR code at your table.</div></div>");
        return sb.ToString();
    }

    private static string Tips(EmailOptions o, bool hasPreOrder) =>
        "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:16px\"><tr>"
        + $"<td width=\"50%\" style=\"background:{Paper};border-radius:12px;padding:12px 14px;font-size:12.5px;line-height:1.5;vertical-align:top\"><b>Finding us</b><br><span style=\"color:{Muted}\">{H(o.Address)}</span></td>"
        + "<td width=\"12\"></td>"
        + $"<td width=\"50%\" style=\"background:{Paper};border-radius:12px;padding:12px 14px;font-size:12.5px;line-height:1.5;vertical-align:top\"><b>At your table</b><br><span style=\"color:{Muted}\">Scan the QR code to check in{(hasPreOrder ? " — your pre-order goes to the kitchen" : "")}.</span></td>"
        + "</tr></table>";

    private static string Page(string kana, string title, string intro, string body, List<(string Text, string Url, bool Solid)> buttons, EmailOptions o, string? remindersOffUrl)
    {
        var btns = string.Join("", buttons.Select(b =>
            $"<a href=\"{H(b.Url)}\" style=\"display:inline-block;margin:0 8px 8px 0;padding:11px 22px;border-radius:99px;font-size:13px;font-weight:700;text-decoration:none;"
            + (b.Solid ? $"background:{Navy};color:#f4f1e9" : $"background:#ffffff;color:{Navy};border:1.5px solid {Navy}") + $"\">{H(b.Text)}</a>"));
        var footer = remindersOffUrl is not null
            ? $"You get this reminder because reminders are on for your account. <a href=\"{H(remindersOffUrl)}\" style=\"color:{Muted}\">Turn off reminders</a>"
            : $"This is a service email about your booking. Replies go to our team{(string.IsNullOrWhiteSpace(o.ReplyTo) ? "" : " at " + H(o.ReplyTo))}.";
        return $"""
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{H(title)}</title></head>
<body style="margin:0;padding:0;background:{Paper};font-family:Arial,Helvetica,sans-serif;color:{Ink}">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Paper}"><tr><td align="center" style="padding:22px 12px">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;border-radius:14px;overflow:hidden">
<tr><td style="background:{Navy};padding:18px 28px">
  <span style="display:inline-block;background:{Rust};color:{Paper};font:11px Georgia,serif;padding:4px 5px;vertical-align:middle">こま</span>
  <span style="color:#f4f1e9;font:17px Georgia,serif;letter-spacing:5px;vertical-align:middle;margin-left:8px">KOMA</span>
  <span style="float:right;color:#f4f1e9;opacity:.15;font:34px Georgia,serif;line-height:1">{kana}</span>
</td></tr>
<tr><td style="padding:26px 30px 22px">
  <div style="font:400 26px/1.2 Georgia,serif;color:{Ink}">{H(title)}</div>
  <div style="font-size:13.5px;line-height:1.6;margin:10px 0 18px;color:#3b4a52">{intro}</div>
  {body}
  <div style="margin-top:22px">{btns}</div>
</td></tr></table>
<div style="max-width:520px;padding:18px 20px 8px;font-size:11px;color:{Muted};line-height:1.6;text-align:center">KOMA · {H(o.Address)} · {H(o.Phone)}<br>{footer}</div>
</td></tr></table></body></html>
""";
    }

    // Calendar file so the booking can be added to Google / Outlook calendar. Malaysia has no daylight saving: UTC = local - 8 h.
    private static MailAttachment Ics(Reservation r, EmailOptions o, int sequence)
    {
        static string U(DateTime local) => local.AddHours(-8).ToString("yyyyMMdd'T'HHmmss'Z'");
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace(",", "\\,").Replace(";", "\\;");
        var ics = string.Join("\r\n",
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//KOMA//Bookings//EN", "METHOD:PUBLISH", "BEGIN:VEVENT",
            $"UID:koma-booking-{r.Id}@koma.my", $"SEQUENCE:{sequence}", $"DTSTAMP:{KOMA.Services.DemoClock.Shared.UtcNow:yyyyMMdd'T'HHmmss'Z'}",
            $"DTSTART:{U(r.StartTime)}", $"DTEND:{U(r.EndTime)}",
            $"SUMMARY:{Esc($"Table at KOMA ({r.GuestCount} guests)")}", $"LOCATION:{Esc(o.Address)}",
            $"DESCRIPTION:{Esc($"Booking ref {Ref(r)}")}", "END:VEVENT", "END:VCALENDAR", "");
        return new MailAttachment($"koma-booking-{Ref(r)}.ics", "text/calendar; charset=utf-8", Encoding.UTF8.GetBytes(ics));
    }
}

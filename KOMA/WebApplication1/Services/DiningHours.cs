using KOMA.Models;

namespace KOMA.Services;

// When the table tablet may take orders (Dining/Menu page and DiningOrderService.PlaceAsync).
// Follows Admin > Settings for THAT day, the same rule as bookings (RestaurantSettings.For):
//   closed weekday or closed date (e.g. Christmas) -> ordering closed all day
//   open day -> orders from that day's opening time until 30 minutes before that day's closing time ("last call"),
//               with a warning in the last 10 minutes before last call.
// (It used to use OpeningTime/ClosingTime, which are only the earliest opening / latest closing of the whole week,
// so closed days, closed dates and shorter days were ignored.)
public record DiningHours(DateTime LastCall, bool Locked, bool Warn, bool ClosedToday = false, string? ClosedReason = null)
{
    public const int LastCallMinutes = 30;

    public static DiningHours At(RestaurantSettings settings, DateTime now)
    {
        // Late-night hours that pass midnight (e.g. 18:00-01:00): just after midnight we are still in yesterday's hours.
        var yesterday = settings.For(DateOnly.FromDateTime(now.Date.AddDays(-1)));
        if (yesterday.IsOpen && yesterday.Closes <= yesterday.Opens && TimeOnly.FromDateTime(now) < yesterday.Closes)
            return Window(now, now.Date.AddDays(-1), yesterday);

        var today = settings.For(DateOnly.FromDateTime(now));
        if (!today.IsOpen)
            return new(now, true, false, true, today.ClosedReason);
        return Window(now, now.Date, today);
    }

    private static DiningHours Window(DateTime now, DateTime day, RestaurantSettings.DayPlan plan)
    {
        var opening = day + plan.Opens.ToTimeSpan();
        var closing = day + plan.Closes.ToTimeSpan();
        if (closing <= opening) closing = closing.AddDays(1);
        var cutoff = closing.AddMinutes(-LastCallMinutes);
        return new(cutoff, now < opening || now >= cutoff, now >= cutoff.AddMinutes(-10) && now < cutoff);
    }

    // Message for a refused order.
    public string ClosedMessage => ClosedToday
        ? $"We're closed today{(string.IsNullOrWhiteSpace(ClosedReason) ? "" : $" ({ClosedReason})")}, so ordering is not available. You can still view your bill."
        : "Ordering is closed. Last call is 30 minutes before closing. You can still view your bill.";
}

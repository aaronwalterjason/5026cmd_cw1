using KOMA.Models;

namespace KOMA.Services;

public record DiningHours(DateTime LastCall, bool Locked, bool Warn)
{
    public static DiningHours At(RestaurantSettings settings, DateTime now)
    {
        var opening = now.Date + settings.OpeningTime.ToTimeSpan();
        var closing = now.Date + settings.ClosingTime.ToTimeSpan();
        if (closing <= opening)
        {
            if (now < closing) opening = opening.AddDays(-1);
            else closing = closing.AddDays(1);
        }
        var cutoff = closing.AddMinutes(-30);
        return new(cutoff, now < opening || now >= cutoff, now >= cutoff.AddMinutes(-10) && now < cutoff);
    }
}

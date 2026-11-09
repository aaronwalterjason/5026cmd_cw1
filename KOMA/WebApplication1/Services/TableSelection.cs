using KOMA.Models;

namespace KOMA.Services;

public record TableSelection(
    DateTime Start,
    DateTime End,
    bool IsNow,
    bool IsOpen)
{
    // Keep the floor and popup on the same instant, even during the first second of a visit.
    public string? SnapshotParameter => IsNow
        ? Start.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture)
        : null;

    public string Label => IsNow
        ? $"{Start:dd MMM yyyy} · As of {Start:HH:mm:ss} (Malaysia time)"
        : $"{Start:dd MMM yyyy} · {Start:HH:mm}–{End:HH:mm} (Malaysia time)";

    public static TableSelection? Resolve(
        RestaurantSettings? settings,
        DateOnly? date,
        TimeOnly? time,
        DateTime? at)
    {
        // The Now button checks an exact moment.
        if (at.HasValue)
        {
            var instant = DateTime.SpecifyKind(
                at.Value,
                DateTimeKind.Unspecified);

            var clock = TimeOnly.FromDateTime(instant);

            var plan = settings?.For(DateOnly.FromDateTime(instant));   // per-day hours and closed dates
            var isOpen =
                plan != null &&
                plan.IsOpen &&
                clock >= plan.Opens &&
                clock < plan.Closes;

            return new TableSelection(
                instant,
                instant,
                true,
                isOpen);
        }

        // Otherwise, use the selected reservation slot.
        if (settings == null ||
            date == null ||
            date == default(DateOnly) ||
            date == DateOnly.MaxValue ||
            time == null ||
            !ReservationRules.Slots(settings).Contains(time.Value))
        {
            return null;
        }

        var start = date.Value.ToDateTime(time.Value);

        return new TableSelection(
            start,
            start.AddMinutes(ReservationRules.SlotMinutes),
            false,
            true);
    }

    public bool Includes(DateTime start, DateTime end)
    {
        return IsNow
            ? start <= Start && end > Start
            : start < End && end > Start;
    }
}
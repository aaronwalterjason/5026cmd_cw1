using KOMA.Models;

namespace KOMA.Services;

public record TableSelection(
    DateTime Start,
    DateTime End,
    bool IsNow,
    bool IsOpen)
{
    public bool CanCreate =>
        !IsNow &&
        IsOpen &&
        Start > ReservationRules.MalaysiaNow;

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

            var isOpen =
                settings != null &&
                clock >= settings.OpeningTime &&
                clock < settings.ClosingTime;

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
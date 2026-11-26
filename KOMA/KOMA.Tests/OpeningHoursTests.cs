using KOMA.Models;
using KOMA.Services;

namespace KOMA.Tests;

// TC-U27..U31: opening hours per day, closed dates and last booking (Admin > Settings).
public class OpeningHoursTests
{
    // Mon closed; Tue-Thu 11-22; Fri 11-23; Sat 10-23; Sun 10-21
    private static RestaurantSettings Make(int lastBooking = 75) => new()
    {
        WeeklyHours = "closed|11:00-22:00|11:00-22:00|11:00-22:00|11:00-23:00|10:00-23:00|10:00-21:00",
        ClosedDates = null, LastBookingMinutes = lastBooking
    };

    [Fact] // TC-U27
    public void Weekly_closed_day_has_no_booking_times()
    {
        var s = Make();
        var monday = new DateOnly(2026, 11, 16);
        Assert.False(s.For(monday).IsOpen);
        Assert.Empty(ReservationRules.Slots(s, monday));
    }

    [Fact] // TC-U28
    public void Closed_date_overrides_the_weekly_hours()
    {
        var s = Make();
        s.SetClosures(new[] { new RestaurantSettings.ClosedPeriod(new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 25), "Christmas") });
        var plan = s.For(new DateOnly(2026, 12, 25));
        Assert.False(plan.IsOpen);
        Assert.Equal("Christmas", plan.ClosedReason);
        Assert.True(s.For(new DateOnly(2026, 12, 26)).IsOpen);   // Saturday after
    }

    [Fact] // TC-U29
    public void Default_last_booking_keeps_the_old_rule()
    {
        // Tue 11:00-22:00, 75 min -> last start 20:45 -> whole hours 11:00 ... 20:00
        var slots = ReservationRules.Slots(Make(), new DateOnly(2026, 11, 17));
        Assert.Equal(new TimeOnly(11, 0), slots.First());
        Assert.Equal(new TimeOnly(20, 0), slots.Last());
        Assert.Equal(10, slots.Count);
    }

    [Fact] // TC-U30
    public void Longer_last_booking_removes_late_slots()
    {
        var slots = ReservationRules.Slots(Make(lastBooking: 120), new DateOnly(2026, 11, 20));   // Fri closes 23:00
        Assert.Equal(new TimeOnly(21, 0), slots.Last());
    }

    [Fact] // TC-U31
    public void Week_is_described_with_grouped_days()
    {
        var text = RestaurantSettings.Describe(Make().WeeklyHours, new(10, 0), new(22, 0));
        Assert.StartsWith("Mon closed · Tue–Thu ", text);
        Assert.Contains("Sun ", text);
    }

    [Fact] // TC-U32
    public void Rows_saved_before_per_day_hours_use_one_time_for_every_day()
    {
        var s = new RestaurantSettings { OpeningTime = new(10, 0), ClosingTime = new(22, 0), WeeklyHours = "" };
        Assert.All(s.Week(), d => { Assert.True(d.IsOpen); Assert.Equal(new TimeOnly(22, 0), d.Closes); });
        Assert.Equal(75, new RestaurantSettings { LastBookingMinutes = 0 }.LastBookingMinutes);
    }
}

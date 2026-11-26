using KOMA.Models;

namespace KOMA.Tests;

// TC-U08..U10: the status badge and wording on Admin > Promotions.
public class PromotionScheduleTests
{
    private static Promotion Make(bool active = true) => new()
    {
        Title = "Happy hour", DiscountPercent = 10, IsActive = active,
        StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 30),
        Days = 127, StartTime = new TimeOnly(17, 0), EndTime = new TimeOnly(19, 0)
    };

    [Theory] // TC-U08
    [InlineData(2026, 11, 9, 18, "Live")]
    [InlineData(2026, 11, 9, 12, "Today")]
    [InlineData(2026, 10, 20, 18, "Scheduled")]
    [InlineData(2026, 12, 2, 18, "Ended")]
    public void Status_follows_the_dates_and_time_window(int y, int m, int d, int h, string expected) =>
        Assert.Equal(expected, Make().StatusAt(new DateTime(y, m, d, h, 0, 0)));

    [Fact] // TC-U09
    public void Paused_promotion_shows_paused() =>
        Assert.Equal("Paused", Make(active: false).StatusAt(new DateTime(2026, 11, 9, 18, 0, 0)));

    [Theory] // TC-U10
    [InlineData(127, "Every day")]
    [InlineData(31, "Mon–Fri")]
    [InlineData(96, "Sat–Sun")]
    [InlineData(5, "Mon, Wed")]
    public void Days_are_written_in_words(int days, string expected) =>
        Assert.Equal(expected, new Promotion { Days = days }.DaysText());
}

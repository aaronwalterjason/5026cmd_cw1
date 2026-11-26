using KOMA.Models;
using KOMA.Services;

namespace KOMA.Tests;

// TC-U22..U25: what Admin > Activity records and how values are shown.
public class ActivityLogTests
{
    [Theory] // TC-U22
    [InlineData(typeof(MenuItem), ActivityLog.Areas.Menu)]
    [InlineData(typeof(Promotion), ActivityLog.Areas.Menu)]
    [InlineData(typeof(Reservation), ActivityLog.Areas.Bookings)]
    [InlineData(typeof(RestaurantTable), ActivityLog.Areas.Floors)]
    [InlineData(typeof(RestaurantSettings), ActivityLog.Areas.Settings)]
    public void Admin_changes_are_logged_under_the_right_area(Type type, string area) =>
        Assert.Equal(area, AuditInterceptor.AreaOf(type));

    [Theory] // TC-U23
    [InlineData(typeof(LandingPageContent))]   // Website editor was removed
    [InlineData(typeof(Order))]                // orders are tracked on the Orders page instead
    public void Other_tables_are_not_logged(Type type) => Assert.Null(AuditInterceptor.AreaOf(type));

    [Theory] // TC-U24
    [InlineData("DiscountPercent", 20.0, "20%")]
    [InlineData("Price", 32.0, "RM 32.00")]
    public void Numbers_are_shown_with_units(string field, double value, string expected) =>
        Assert.Equal(expected, ActivityFormat.Value(field, (decimal)value));

    [Fact] // TC-U25
    public void Promotion_fields_read_as_words()
    {
        Assert.Equal("Mon–Fri", ActivityFormat.Value("Days", 31));
        Assert.Equal("On", ActivityFormat.Value("IsActive", true));
        Assert.Equal("9 Nov 2026", ActivityFormat.Value("StartDate", new DateTime(2026, 11, 9)));
        Assert.Equal("Running", ActivityFormat.Label(typeof(Promotion), "IsActive"));
        Assert.Equal("Category", ActivityFormat.Label(typeof(MenuItem), "MenuCategoryId"));
        Assert.Equal("(empty)", ActivityFormat.Value("BannerText", "  "));
    }
}

using KOMA.Models;
using KOMA.Services;

namespace KOMA.Tests;

// TC-U01..U07: promotion discounts are charged at the right price and only when the schedule applies.
public class MenuPricingTests
{
    // Monday 9 Nov 2026, 1:00 pm
    private static readonly DateTime MonLunch = new(2026, 11, 9, 13, 0, 0);

    private static Promotion Lunch(decimal percent = 20) => new()
    {
        Id = 1, Title = "Lunch deal", DiscountPercent = percent, IsActive = true,
        StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 30),
        Days = 31,                                   // Mon-Fri
        StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(15, 0)
    };

    private static MenuItem Dish(decimal price, Promotion? promo = null) =>
        new() { Id = 7, Name = "Salmon Don", Price = price, Promotion = promo, PromotionId = promo?.Id };

    [Fact] // TC-U01
    public void No_promotion_charges_the_menu_price()
    {
        var price = MenuPricing.For(Dish(32m), MonLunch);
        Assert.Equal(32m, price.Unit);
        Assert.False(price.Discounted);
    }

    [Fact] // TC-U02
    public void Running_promotion_takes_the_percentage_off()
    {
        var price = MenuPricing.For(Dish(32m, Lunch(20)), MonLunch);
        Assert.Equal(25.60m, price.Unit);
        Assert.Equal(32m, price.List);
        Assert.True(price.Discounted);
        Assert.Equal(20, price.Percent);
    }

    [Fact] // TC-U03
    public void Discount_is_rounded_to_the_nearest_sen()
    {
        // 9.99 x 0.85 = 8.4915 -> 8.49
        Assert.Equal(8.49m, MenuPricing.For(Dish(9.99m, Lunch(15)), MonLunch).Unit);
    }

    [Theory] // TC-U04
    [InlineData(2026, 11, 14, 13)]   // Saturday - not a Mon-Fri day
    [InlineData(2026, 11, 9, 18)]    // Monday evening - outside 11 am-3 pm
    [InlineData(2026, 12, 1, 13)]    // after the end date
    public void Promotion_outside_its_schedule_is_not_applied(int y, int m, int d, int h)
    {
        var price = MenuPricing.For(Dish(32m, Lunch()), new DateTime(y, m, d, h, 0, 0));
        Assert.Equal(32m, price.Unit);
        Assert.False(price.Discounted);
    }

    [Fact] // TC-U05
    public void Paused_promotion_is_not_applied()
    {
        var promo = Lunch(); promo.IsActive = false;
        Assert.Equal(32m, MenuPricing.For(Dish(32m, promo), MonLunch).Unit);
    }

    [Fact] // TC-U06
    public void Order_line_records_the_charged_and_list_price()
    {
        var line = new OrderItem();
        MenuPricing.Apply(line, Dish(32m, Lunch(25)), MonLunch);
        Assert.Equal(24m, line.UnitPrice);
        Assert.Equal(32m, line.ListPrice);
        Assert.Equal("Lunch deal", line.PromotionTitle);
    }

    [Fact] // TC-U07
    public void Order_line_without_discount_has_no_list_price()
    {
        var line = new OrderItem();
        MenuPricing.Apply(line, Dish(32m), MonLunch);
        Assert.Equal(32m, line.UnitPrice);
        Assert.Null(line.ListPrice);
        Assert.Null(line.PromotionTitle);
    }
    [Theory] // TC-U26: boundary value analysis of the schedule (dates and times are inclusive)
    [InlineData(2026, 10, 31, 13, 0, 127, false)]   // day before the start date
    [InlineData(2026, 11, 1, 13, 0, 127, true)]     // start date
    [InlineData(2026, 11, 30, 13, 0, 127, true)]    // end date
    [InlineData(2026, 12, 1, 13, 0, 127, false)]    // day after the end date
    [InlineData(2026, 11, 13, 13, 0, 31, true)]     // Friday - last weekday in Mon-Fri
    [InlineData(2026, 11, 14, 13, 0, 31, false)]    // Saturday
    [InlineData(2026, 11, 9, 10, 59, 31, false)]    // one minute before the window
    [InlineData(2026, 11, 9, 11, 0, 31, true)]      // window starts
    [InlineData(2026, 11, 9, 15, 0, 31, true)]      // window ends (inclusive)
    [InlineData(2026, 11, 9, 15, 1, 31, false)]     // one minute after the window
    public void Schedule_boundaries_are_inclusive(int y, int m, int d, int h, int min, int days, bool discounted)
    {
        var promo = Lunch(); promo.Days = days;
        var price = MenuPricing.For(Dish(32m, promo), new DateTime(y, m, d, h, min, 0));
        Assert.Equal(discounted, price.Discounted);
    }
    [Fact] // TC-U33: promotions apply to tablet orders only - pre-orders pay the menu price
    public void Pre_order_lines_use_the_menu_price_and_tablet_lines_the_promotion()
    {
        var menu = new[] { Dish(32m, Lunch(20)) };
        var qty = new Dictionary<int, int> { [7] = 2 };
        var pre = OrderingMenu.BuildLines(menu, qty, null).Single();
        var tablet = OrderingMenu.BuildLines(menu, qty, MonLunch).Single();
        Assert.Equal(32m, pre.UnitPrice);
        Assert.Null(pre.ListPrice);
        Assert.Equal(25.60m, tablet.UnitPrice);
        Assert.Equal(32m, tablet.ListPrice);
    }
}

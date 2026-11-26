using KOMA.Services.Reporting;

namespace KOMA.Tests;

// TC-U11..U15: Day / Week / Month periods used by Reports and Revenue.
public class ReportPeriodTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 10, 0, 0);   // Sunday

    [Fact] // TC-U11
    public void Week_runs_Monday_to_Sunday()
    {
        var p = new ReportPeriod("week", new DateTime(2026, 11, 12), null, Now);   // a Thursday
        Assert.Equal(new DateTime(2026, 11, 9), p.Start);
        Assert.Equal(new DateTime(2026, 11, 16), p.End);
        Assert.Equal("9–15 Nov 2026", p.Label);
    }

    [Fact] // TC-U12
    public void Month_covers_the_whole_calendar_month()
    {
        var p = new ReportPeriod("month", null, "2026-02", Now);
        Assert.Equal(new DateTime(2026, 2, 1), p.Start);
        Assert.Equal(new DateTime(2026, 3, 1), p.End);
        Assert.True(p.HasNext);
    }

    [Fact] // TC-U13
    public void Unknown_period_falls_back_to_this_month()
    {
        var p = new ReportPeriod("fortnight", null, null, Now);
        Assert.Equal("month", p.Kind);
        Assert.Equal(new DateTime(2026, 11, 1), p.Start);
        Assert.True(p.InProgress);
        Assert.False(p.HasNext);   // can't step into the future
    }

    [Theory] // TC-U14
    [InlineData("2026-11-30", "2026-12-06", "30 Nov – 6 Dec 2026")]
    [InlineData("2025-12-29", "2026-01-04", "29 Dec 2025 – 4 Jan 2026")]
    [InlineData("2026-11-09", "2026-11-09", "9 Nov 2026")]
    public void Spans_across_months_and_years_read_correctly(string a, string b, string expected) =>
        Assert.Equal(expected, ReportPeriod.Span(DateTime.Parse(a), DateTime.Parse(b)));

    [Fact] // TC-U15
    public void Previous_day_route_steps_back_one_day()
    {
        var p = new ReportPeriod("day", new DateTime(2026, 11, 1), null, Now);
        Assert.Equal("2026-10-31", p.PrevRoute["date"]);
        Assert.Equal("2026-11-02", p.NextRoute["date"]);
    }
}

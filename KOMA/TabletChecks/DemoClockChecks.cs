using KOMA.Services;
using KOMA.Pages;
using Microsoft.AspNetCore.Mvc;

internal static class DemoClockChecks
{
    public static void Run(Action<bool, string> check)
    {
        var source = new ManualTime();
        var clock = new DemoClock(source);
        check(!clock.Set(new DateTime(2026, 11, 9, 12, 0, 0)), "Simulation rejects changes when disabled");
        clock.Configure(true);
        var version = clock.Snapshot().Version;
        check(clock.Set(new DateTime(2027, 1, 1, 0, 5, 0)), "Demo date can move into another year");
        check(clock.MalaysiaNow == new DateTime(2027, 1, 1, 0, 5, 0), "Chosen wall time is interpreted as Malaysia time");
        check(clock.UtcNow == new DateTime(2026, 12, 31, 16, 5, 0, DateTimeKind.Utc), "UTC record timestamps follow the same simulated instant");
        check(clock.Snapshot().Version != version, "Changing demo time notifies connected screens through a new revision");
        source.Advance(TimeSpan.FromMinutes(3));
        check(clock.MalaysiaNow.Minute == 8, "Demo time keeps ticking normally");
        version = clock.Snapshot().Version;
        source.Advance(TimeSpan.FromSeconds(1));
        check(clock.Snapshot().Version == version, "Normal ticking does not trigger page reloads");
        check(clock.Set(new DateTime(2026, 11, 9, 11, 50, 0)), "Demo clock can move backwards for a new scenario");
        var hours = new RestaurantSettingsForCheck();
        clock.Set(new DateTime(2026, 11, 9, 21, 20, 0));
        check(DiningHours.At(hours.Value, clock.MalaysiaNow).Warn, "Simulated time drives the last-call warning");
        source.Advance(TimeSpan.FromMinutes(10));
        check(DiningHours.At(hours.Value, clock.MalaysiaNow).Locked, "Running simulated clock reaches the ordering cutoff");
        check(!clock.Set(new DateTime(1999, 1, 1)), "Out-of-range simulator dates are rejected");
        clock.Reset();
        check(!clock.Snapshot().Simulated && clock.UtcNow == source.GetUtcNow().UtcDateTime, "Reset restores current real time");
        clock.Set(new DateTime(2028, 1, 1));
        clock.Configure(false);
        check(!clock.Snapshot().Enabled && !clock.Snapshot().Simulated && !clock.Reset(), "Disabling demo mode clears simulation and prevents reset operations");

        var page = new DemoTimeModel();
        DemoClock.Shared.Configure(false);
        check(page.OnGet() is NotFoundResult && page.OnPostSet("2026-11-09T12:00") is NotFoundResult, "Simulator endpoint is unavailable outside enabled demo mode");
        DemoClock.Shared.Configure(true);
        try
        {
            check(page.OnPostSet("invalid") is BadRequestObjectResult, "Simulator endpoint rejects malformed dates");
            check(page.OnPostSet("2026-11-09T12:00") is JsonResult, "Simulator endpoint accepts a selected date and time");
            check(ReservationRules.MalaysiaNow.Date == new DateTime(2026, 11, 9), "Reservation rules use the shared demo clock");
            check(page.OnPostReset() is JsonResult && !DemoClock.Shared.Snapshot().Simulated, "Simulator reset endpoint returns real-time state");
        }
        finally { DemoClock.Shared.Configure(false); }
    }
    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 9, 4, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }
    private sealed class RestaurantSettingsForCheck
    {
        public KOMA.Models.RestaurantSettings Value { get; } = new() { OpeningTime = new(10, 0), ClosingTime = new(22, 0) };
    }
}

namespace KOMA.Services;

// One application-wide clock: requests and background dining workers share the same timeline.
// Security tokens, cookie expiry and cache lifetimes deliberately retain real elapsed time.
public sealed class DemoClock(TimeProvider? provider = null)
{
    public static DemoClock Shared { get; } = new();
    private readonly TimeProvider time = provider ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly string instance = Guid.NewGuid().ToString("N");
    private TimeSpan offset;
    private bool enabled, simulated;
    private long revision;
    public record State(bool Enabled, bool Simulated, DateTimeOffset Now, string Version);

    public State Snapshot()
    {
        lock (gate)
            return new(enabled, simulated, (time.GetUtcNow() + offset).ToOffset(TimeSpan.FromHours(8)), $"{instance}:{revision}");
    }
    public DateTime MalaysiaNow => Snapshot().Now.DateTime;
    public DateTime UtcNow => Snapshot().Now.UtcDateTime;
    public void Configure(bool allowSimulation)
    {
        lock (gate) { enabled = allowSimulation; offset = TimeSpan.Zero; simulated = false; revision++; }
    }
    public bool Set(DateTime malaysiaTime)
    {
        if (malaysiaTime.Year is < 2000 or > 2100) return false;
        lock (gate)
        {
            if (!enabled) return false;
            offset = new DateTimeOffset(DateTime.SpecifyKind(malaysiaTime, DateTimeKind.Unspecified), TimeSpan.FromHours(8)) - time.GetUtcNow();
            simulated = true; revision++;
            return true;
        }
    }
    public bool Reset()
    {
        lock (gate)
        {
            if (!enabled) return false;
            offset = TimeSpan.Zero; simulated = false; revision++;
            return true;
        }
    }
}

namespace NasMonitor.Core.Diagnostics;

/// <summary>Synthetic data only; never connects to a NAS.</summary>
public sealed record DemoSnapshot(double Cpu, double Ram, DateTimeOffset CollectedAt)
{
    /// <summary>Deterministic bounded sample shared by all views.</summary>
    public static DemoSnapshot Create(long tick, DateTimeOffset timestamp)
    {
        return new DemoSnapshot(35 + 24 * Math.Sin(tick / 7.0), 58 + 8 * Math.Cos(tick / 13.0), timestamp);
    }
}

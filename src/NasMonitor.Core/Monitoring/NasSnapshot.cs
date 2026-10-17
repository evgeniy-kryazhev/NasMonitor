namespace NasMonitor.Core.Monitoring;

/// <summary>Immutable read-only resource sample for one NAS, without credentials.</summary>
public sealed record NasSnapshot(
    Guid NasId,
    DateTimeOffset CollectedAt,
    NasMetric Cpu,
    NasMetric Ram,
    NasMetric? Storage = null,
    NasMetric? DownloadBytesPerSecond = null,
    NasMetric? UploadBytesPerSecond = null,
    NasMetric? TemperatureCelsius = null,
    NasMetric? UptimeSeconds = null,
    string? Model = null,
    string? DsmVersion = null,
    IReadOnlyList<NasVolume>? Volumes = null)
{
    /// <summary>All displayed metrics with independent availability and timestamps.</summary>
    public IEnumerable<NasMetric?> Metrics =>
    [
        Cpu, Ram, Storage, DownloadBytesPerSecond, UploadBytesPerSecond,
        TemperatureCelsius, UptimeSeconds
    ];
}

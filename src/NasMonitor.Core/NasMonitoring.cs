namespace NasMonitor.Core;

/// <summary>Availability of one normalized metric; missing values are never zero.</summary>
public enum MetricAvailability
{
    Available,
    Unavailable,
    Invalid,
    PermissionDenied,
    Unsupported,
    Stale
}

/// <summary>A numeric metric with its own collection time and availability; units belong to the snapshot field.</summary>
public sealed record NasMetric(double? Value, DateTimeOffset CollectedAt, MetricAvailability Availability)
{
    /// <summary>Accept only finite values in the specified inclusive range (percentages by default).</summary>
    public static NasMetric Create(double? value, DateTimeOffset collectedAt, double minimum = 0, double maximum = 100)
    {
        if (value is null)
        {
            return new(null, collectedAt, MetricAvailability.Unavailable);
        }
        if (!double.IsFinite(value.Value) || value < minimum || value > maximum)
        {
            return new(null, collectedAt, MetricAvailability.Invalid);
        }
        return new(value, collectedAt, MetricAvailability.Available);
    }
}

/// <summary>Immutable read-only resource sample for one NAS, without credentials.</summary>
public sealed record NasSnapshot(Guid NasId, DateTimeOffset CollectedAt, NasMetric Cpu, NasMetric Ram,
    NasMetric? Storage = null, NasMetric? DownloadBytesPerSecond = null, NasMetric? UploadBytesPerSecond = null,
    NasMetric? TemperatureCelsius = null, NasMetric? UptimeSeconds = null, string? Model = null,
    string? DsmVersion = null, IReadOnlyList<NasVolume>? Volumes = null)
{
    /// <summary>All displayed metrics with independent availability and timestamps.</summary>
    public IEnumerable<NasMetric?> Metrics => [Cpu, Ram, Storage, DownloadBytesPerSecond, UploadBytesPerSecond,
        TemperatureCelsius, UptimeSeconds];
}

/// <summary>A logical volume; aggregate storage uses sum(used) / sum(total), not average percentages.</summary>
public sealed record NasVolume(string Id, double UsedBytes, double TotalBytes);

/// <summary>Independent state of a NAS monitor.</summary>
public enum NasMonitorStatus
{
    Connecting,
    Online,
    Degraded,
    Offline,
    AuthenticationRequired,
    PermissionDenied,
    Unsupported,
    Disabled,
    Stopped
}

/// <summary>Cached state; a failed attempt keeps the last sample explicitly stale.</summary>
public sealed record NasMonitorState(Guid NasId, NasMonitorStatus Status, NasSnapshot? Snapshot = null,
    DateTimeOffset? LastAttemptAt = null, DateTimeOffset? LastSuccessAt = null,
    ConnectionError Error = ConnectionError.None, int? DsmCode = null)
{
    /// <summary>True when the sample is no longer evidence of a successful current poll.</summary>
    public bool IsStale => Snapshot is not null && Status is not (NasMonitorStatus.Online or NasMonitorStatus.Degraded);
}

/// <summary>Classified provider failure containing no response, URL or secret.</summary>
public sealed class NasProviderException(ConnectionError error, int? dsmCode = null) : Exception("nas.provider-failed")
{
    /// <summary>Safe failure classification.</summary>
    public ConnectionError Error { get; } = error;
    /// <summary>Optional numeric API error.</summary>
    public int? DsmCode { get; } = dsmCode;
}

/// <summary>Authenticated provider session; implementation owns SID and cleanup.</summary>
public interface INasMetricsSession : IAsyncDisposable
{
    /// <summary>Collect one sample; never start a polling loop here.</summary>
    Task<NasSnapshot> ReadAsync(CancellationToken cancellationToken, bool forceRefresh = false);
}

/// <summary>Create an isolated authenticated session for a NAS.</summary>
public interface INasMetricsSessionFactory
{
    /// <summary>Discover required APIs and authenticate with short-lived input credentials.</summary>
    Task<INasMetricsSession> ConnectAsync(NasProfile profile, string password, string? otp,
        CancellationToken cancellationToken);
}

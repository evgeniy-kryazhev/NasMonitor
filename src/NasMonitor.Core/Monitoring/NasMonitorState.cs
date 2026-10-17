using NasMonitor.Core.Connections;

namespace NasMonitor.Core.Monitoring;

/// <summary>Cached state; a failed attempt keeps the last sample explicitly stale.</summary>
public sealed record NasMonitorState(
    Guid NasId,
    NasMonitorStatus Status,
    NasSnapshot? Snapshot = null,
    DateTimeOffset? LastAttemptAt = null,
    DateTimeOffset? LastSuccessAt = null,
    ConnectionError Error = ConnectionError.None,
    int? DsmCode = null)
{
    /// <summary>True when the sample is no longer evidence of a successful current poll.</summary>
    public bool IsStale => Snapshot is not null && Status is not (NasMonitorStatus.Online or NasMonitorStatus.Degraded);
}

namespace NasMonitor.Core.Monitoring;

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

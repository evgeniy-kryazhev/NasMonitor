namespace NasMonitor.Core.Monitoring;

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

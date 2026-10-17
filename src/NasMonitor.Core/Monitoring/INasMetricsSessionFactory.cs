using NasMonitor.Core.Profiles;

namespace NasMonitor.Core.Monitoring;

/// <summary>Create an isolated authenticated session for a NAS.</summary>
public interface INasMetricsSessionFactory
{
    /// <summary>Discover required APIs and authenticate with short-lived input credentials.</summary>
    public Task<INasMetricsSession> ConnectAsync(NasProfile profile,
        string password,
        string? otp,
        CancellationToken cancellationToken);
}

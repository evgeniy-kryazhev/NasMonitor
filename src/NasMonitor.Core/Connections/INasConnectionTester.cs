using NasMonitor.Core.Profiles;

namespace NasMonitor.Core.Connections;

/// <summary>Tests a connection with short-lived credentials and closes its temporary session.</summary>
public interface INasConnectionTester
{
    /// <summary>Check transport, discover APIs, log in once and log out without polling.</summary>
    public Task<NasConnectionResult> TestAsync(NasProfile profile,
        string password,
        string? otp,
        IProgress<ConnectionCheck>? progress,
        CancellationToken cancellationToken);
}

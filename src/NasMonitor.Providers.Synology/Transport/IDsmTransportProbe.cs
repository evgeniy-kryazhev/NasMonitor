using NasMonitor.Core.Connections;

namespace NasMonitor.Providers.Synology.Transport;

/// <summary>Separates DNS/TCP/TLS evidence from DSM JSON parsing.</summary>
public interface IDsmTransportProbe
{
    /// <summary>Open a temporary transport with normal OS certificate and hostname validation.</summary>
    public Task ProbeAsync(Uri endpoint, Action<ConnectionCheck> report, CancellationToken cancellationToken);
}

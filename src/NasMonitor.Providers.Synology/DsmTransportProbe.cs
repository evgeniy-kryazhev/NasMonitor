using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using NasMonitor.Core;

namespace NasMonitor.Providers.Synology;

/// <summary>Separates DNS/TCP/TLS evidence from DSM JSON parsing.</summary>
public interface IDsmTransportProbe
{
    /// <summary>Open a temporary transport with normal OS certificate and hostname validation.</summary>
    Task ProbeAsync(Uri endpoint, Action<ConnectionCheck> report, CancellationToken cancellationToken);
}

/// <summary>Checks connectivity without sending a username, password or application request.</summary>
public sealed class DsmTransportProbe : IDsmTransportProbe
{
    /// <inheritdoc />
    public async Task ProbeAsync(Uri endpoint, Action<ConnectionCheck> report, CancellationToken cancellationToken)
    {
        await Dns.GetHostAddressesAsync(endpoint.DnsSafeHost, cancellationToken).ConfigureAwait(false);
        report(new(ConnectionStage.Dns, CheckState.Passed));
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(endpoint.DnsSafeHost, endpoint.Port, cancellationToken).ConfigureAwait(false);
        report(new(ConnectionStage.Tcp, CheckState.Passed));
        using var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
        var options = new SslClientAuthenticationOptions { TargetHost = endpoint.DnsSafeHost };
        await tls.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
        report(new(ConnectionStage.Tls, CheckState.Passed));
    }
}

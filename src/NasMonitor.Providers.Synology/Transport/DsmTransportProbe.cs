using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using NasMonitor.Core.Connections;

namespace NasMonitor.Providers.Synology.Transport;

/// <summary>Checks connectivity without sending a username, password or application request.</summary>
public sealed class DsmTransportProbe : IDsmTransportProbe
{
    /// <inheritdoc />
    public async Task ProbeAsync(Uri endpoint, Action<ConnectionCheck> report, CancellationToken cancellationToken)
    {
        await Dns.GetHostAddressesAsync(endpoint.DnsSafeHost, cancellationToken).ConfigureAwait(false);
        report(new ConnectionCheck(ConnectionStage.Dns, CheckState.Passed));
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(endpoint.DnsSafeHost, endpoint.Port, cancellationToken).ConfigureAwait(false);
        report(new ConnectionCheck(ConnectionStage.Tcp, CheckState.Passed));
        using var tls = new SslStream(tcp.GetStream(), false);
        var options = new SslClientAuthenticationOptions { TargetHost = endpoint.DnsSafeHost };
        await tls.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
        report(new ConnectionCheck(ConnectionStage.Tls, CheckState.Passed));
    }
}

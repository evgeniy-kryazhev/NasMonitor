using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.Core.Connections;
using NasMonitor.Core.Profiles;
using NasMonitor.Providers.Synology.Connections;
using NasMonitor.Providers.Synology.Transport;

namespace NasMonitor.Connection.Tests.Connections;

[TestClass]
public sealed class TlsTests
{
    [TestMethod]
    public async Task UntrustedSelfSignedCertificateIsRejectedBeforeLogin()
    {
        using var key = RSA.Create(2048);
        var certificateRequest =
            new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        certificateRequest.CertificateExtensions.Add(names.Build());
        using X509Certificate2 certificate = certificateRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task server = AcceptTlsAsync(listener, certificate, timeout.Token);
        using var handler = new RejectAllHttpHandler();
        using var http = new HttpClient(handler);
        var tester = new SynologyConnectionTester(http, new DsmTransportProbe());
        var profile = new NasProfile(Guid.NewGuid(), "TLS fixture", new Uri($"https://localhost:{port}"), "fixture");

        NasConnectionResult result = await tester.TestAsync(profile, "synthetic-password", null, null, timeout.Token);
        await server;

        Assert.IsFalse(result.Authenticated);
        Assert.AreEqual(ConnectionStage.Tls, result.Checks.Last().Stage);
        Assert.AreEqual(ConnectionError.Tls, result.Checks.Last().Error);
        Assert.AreEqual(0, handler.RequestCount);
    }

    private static async Task AcceptTlsAsync(TcpListener listener,
        X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        using TcpClient connection = await listener.AcceptTcpClientAsync(cancellationToken);
        using var stream = new SslStream(connection.GetStream());
        try
        {
            await stream.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions { ServerCertificate = certificate },
                cancellationToken);
            // Keep the fixture open until the client rejects the certificate and closes it.
            byte[] buffer = new byte[1];
            int bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            Assert.AreEqual(0, bytesRead);
        }
        catch (Exception exception) when (exception is AuthenticationException or IOException)
        {
            // A TLS alert from the client is an expected outcome for this untrusted fixture.
        }
    }

    private sealed class RejectAllHttpHandler : HttpMessageHandler
    {
        public int RequestCount
        {
            get;
            private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new InvalidOperationException("No DSM request may follow a failed TLS probe.");
        }
    }
}

using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.Core.Connections;
using NasMonitor.Core.Profiles;
using NasMonitor.Providers.Synology.Connections;
using NasMonitor.Providers.Synology.Transport;

namespace NasMonitor.Connection.Tests.Connections;

[TestClass]
public sealed class DsmConnectionTests
{
    private const string Discovery =
        """{"success":true,"data":{"SYNO.API.Auth":{"path":"auth.cgi","minVersion":3,"maxVersion":7}}}""";

    private const string Login = """{"success":true,"data":{"sid":"synthetic-session","synotoken":"synthetic-csrf"}}""";

    private const string Capabilities =
        """{"success":true,"data":{"SYNO.Core.System":{"path":"entry.cgi","minVersion":1,"maxVersion":3}}}""";

    private static NasProfile Profile =>
        new(Guid.NewGuid(), "Test NAS", new Uri("https://nas.example:5001"), "test-user");

    [TestMethod]
    public async Task SuccessfulLoginUsesPostAndDiscoveredPathThenLogsOut()
    {
        using var handler = new ScriptedHandler([Discovery, Login, Capabilities, """{"success":true}"""]);
        using var client = new HttpClient(handler);
        var tester = new SynologyConnectionTester(client, new SuccessfulTransport());

        NasConnectionResult result =
            await tester.TestAsync(Profile, "test &+пароль", "123456", null, CancellationToken.None);

        Assert.IsTrue(result.Authenticated);
        Assert.AreEqual(4, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.All(request => request.Method == HttpMethod.Post && request.Uri.Query == ""));
        Assert.AreEqual("/webapi/auth.cgi", handler.Requests[1].Uri.AbsolutePath);
        Assert.AreEqual("test &+пароль", handler.Requests[1].Form["passwd"]);
        Assert.AreEqual("123456", handler.Requests[1].Form["otp_code"]);
        Assert.AreEqual("6", handler.Requests[1].Form["version"]);
        Assert.AreEqual("synthetic-csrf", handler.Requests[2].Form["SynoToken"]);
        Assert.AreEqual("synthetic-session", handler.Requests[3].Form["_sid"]);
        Assert.AreEqual("logout", handler.Requests[3].Form["method"]);
        Assert.AreEqual(CheckState.Unverified,
            result.Checks.Single(check => check.Stage == ConnectionStage.Permissions).State);
        string resultJson = JsonSerializer.Serialize(result);
        Assert.IsFalse(resultJson.Contains("synthetic-session", StringComparison.Ordinal));
        Assert.IsFalse(resultJson.Contains("synthetic-csrf", StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow(400, ConnectionError.InvalidCredentials)]
    [DataRow(401, ConnectionError.AccountDisabled)]
    [DataRow(402, ConnectionError.PermissionDenied)]
    [DataRow(403, ConnectionError.OtpRequired)]
    [DataRow(404, ConnectionError.InvalidOtp)]
    [DataRow(406, ConnectionError.OtpRequired)]
    [DataRow(407, ConnectionError.BlockedIp)]
    [DataRow(409, ConnectionError.PasswordChangeRequired)]
    [DataRow(105, ConnectionError.PermissionDenied)]
    public async Task LoginErrorsAreClassifiedWithoutRetryingPassword(int code, ConnectionError expected)
    {
        string failure = JsonSerializer.Serialize(new { success = false, error = new { code } });
        using var handler = new ScriptedHandler([Discovery, failure]);
        using var client = new HttpClient(handler);
        var tester = new SynologyConnectionTester(client, new SuccessfulTransport());

        NasConnectionResult result =
            await tester.TestAsync(Profile, "synthetic-password", null, null, CancellationToken.None);

        Assert.IsFalse(result.Authenticated);
        Assert.AreEqual(expected, result.Checks.Last().Error);
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("https://other.example/steal")]
    [DataRow("../outside.cgi")]
    [DataRow("%2e%2e/steal.cgi")]
    [DataRow("/entry.cgi")]
    [DataRow("entry.cgi?capture=yes")]
    public async Task UntrustedDiscoveryPathNeverReceivesPassword(string path)
    {
        string discovery = JsonSerializer.Serialize(new
        {
            success = true,
            data = new Dictionary<string, object>
            {
                ["SYNO.API.Auth"] = new { path, minVersion = 3, maxVersion = 7 }
            }
        });
        using var handler = new ScriptedHandler([discovery]);
        using var client = new HttpClient(handler);
        var tester = new SynologyConnectionTester(client, new SuccessfulTransport());

        NasConnectionResult result =
            await tester.TestAsync(Profile, "synthetic-password", null, null, CancellationToken.None);

        Assert.AreEqual(ConnectionError.InvalidResponse, result.Checks.Last().Error);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task UnsupportedAuthVersionStopsBeforeLogin()
    {
        using var handler = new ScriptedHandler([Discovery.Replace("\"maxVersion\":7", "\"maxVersion\":5")]);
        using var client = new HttpClient(handler);
        var tester = new SynologyConnectionTester(client, new SuccessfulTransport());

        NasConnectionResult result =
            await tester.TestAsync(Profile, "synthetic-password", null, null, CancellationToken.None);

        Assert.AreEqual(ConnectionError.UnsupportedApi, result.Checks.Last().Error);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task CapabilitiesFailureStillLogsOutAuthenticatedSession()
    {
        using var handler = new ScriptedHandler([
            Discovery, Login, """{"success":false,"error":{"code":105}}""", """{"success":true}"""
        ]);
        using var client = new HttpClient(handler);
        var tester = new SynologyConnectionTester(client, new SuccessfulTransport());

        NasConnectionResult result =
            await tester.TestAsync(Profile, "synthetic-password", null, null, CancellationToken.None);

        Assert.IsTrue(result.Authenticated);
        Assert.AreEqual(ConnectionError.PermissionDenied,
            result.Checks.Single(check => check.Stage == ConnectionStage.Capabilities).Error);
        Assert.AreEqual(ConnectionStage.Logout, result.Checks.Last().Stage);
        Assert.AreEqual(CheckState.Passed, result.Checks.Last().State);
    }

    [TestMethod]
    public async Task CancellationAfterLoginUsesIndependentLogoutToken()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new ScriptedHandler([Discovery, Login, Capabilities, """{"success":true}"""])
        {
            BeforeResponse = index =>
            {
                if (index == 2)
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                }
            }
        };
        using var client = new HttpClient(handler);
        var tester = new SynologyConnectionTester(client, new SuccessfulTransport());

        NasConnectionResult result =
            await tester.TestAsync(Profile, "synthetic-password", null, null, cancellation.Token);

        Assert.AreEqual(ConnectionError.Cancelled,
            result.Checks.Single(check => check.Stage == ConnectionStage.Capabilities).Error);
        Assert.AreEqual(CheckState.Passed, result.Checks.Last().State);
        Assert.AreEqual("logout", handler.Requests.Last().Form["method"]);
    }

    [TestMethod]
    public async Task TlsFailureStopsBeforeAnyDsmRequest()
    {
        using var handler = new ScriptedHandler([]);
        using var client = new HttpClient(handler);
        var tester = new SynologyConnectionTester(client, new FailedTlsTransport());

        NasConnectionResult result =
            await tester.TestAsync(Profile, "synthetic-password", null, null, CancellationToken.None);

        Assert.AreEqual(ConnectionStage.Tls, result.Checks.Last().Stage);
        Assert.AreEqual(ConnectionError.Tls, result.Checks.Last().Error);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task MalformedJsonFailsWithoutEchoingResponse()
    {
        using var handler = new ScriptedHandler(["<html>synthetic-secret</html>"]);
        using var client = new HttpClient(handler);
        var tester = new SynologyConnectionTester(client, new SuccessfulTransport());

        NasConnectionResult result =
            await tester.TestAsync(Profile, "synthetic-password", null, null, CancellationToken.None);

        Assert.AreEqual(ConnectionError.InvalidResponse, result.Checks.Last().Error);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("synthetic-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DisabledProfileAndMissingPasswordNeverReachTransport()
    {
        using var handler = new ScriptedHandler([]);
        using var client = new HttpClient(handler);
        var tester = new SynologyConnectionTester(client, new FailedTlsTransport());

        NasConnectionResult disabled = await tester.TestAsync(Profile with { Enabled = false },
            "test",
            null,
            null,
            CancellationToken.None);
        NasConnectionResult missing = await tester.TestAsync(Profile, "", null, null, CancellationToken.None);

        Assert.AreEqual(ConnectionError.Disabled, disabled.Checks.Single().Error);
        Assert.AreEqual(ConnectionError.MissingPassword, missing.Checks.Single().Error);
    }

    [TestMethod]
    public async Task RedirectResponseIsRejectedBeforeLogin()
    {
        using var handler = new ScriptedHandler([Discovery]) { ResponseStatus = HttpStatusCode.Redirect };
        using var client = new HttpClient(handler);
        var tester = new SynologyConnectionTester(client, new SuccessfulTransport());

        NasConnectionResult result =
            await tester.TestAsync(Profile, "synthetic-password", null, null, CancellationToken.None);

        Assert.AreEqual(ConnectionError.Http, result.Checks.Last().Error);
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.IsFalse(handler.Requests.Single().Form.ContainsKey("passwd"));
    }

    [TestMethod]
    public async Task MissingAuthApiIsUnsupportedRatherThanSuccessfulDiscovery()
    {
        using var handler = new ScriptedHandler(["""{"success":true,"data":{}}"""]);
        using var client = new HttpClient(handler);
        var tester = new SynologyConnectionTester(client, new SuccessfulTransport());

        NasConnectionResult result =
            await tester.TestAsync(Profile, "synthetic-password", null, null, CancellationToken.None);

        Assert.AreEqual(ConnectionError.UnsupportedApi, result.Checks.Last().Error);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task FailedLogoutIsReportedWithoutLeakingSessionId()
    {
        using var handler =
            new ScriptedHandler([Discovery, Login, Capabilities, """{"success":false,"error":{"code":119}}"""]);
        using var client = new HttpClient(handler);
        var tester = new SynologyConnectionTester(client, new SuccessfulTransport());

        NasConnectionResult result =
            await tester.TestAsync(Profile, "synthetic-password", null, null, CancellationToken.None);

        Assert.IsTrue(result.Authenticated);
        Assert.AreEqual(ConnectionStage.Logout, result.Checks.Last().Stage);
        Assert.AreEqual(CheckState.Failed, result.Checks.Last().State);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("synthetic-session", StringComparison.Ordinal));
    }

    private sealed class SuccessfulTransport : IDsmTransportProbe
    {
        public Task ProbeAsync(Uri endpoint, Action<ConnectionCheck> report, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            report(new ConnectionCheck(ConnectionStage.Dns, CheckState.Passed));
            report(new ConnectionCheck(ConnectionStage.Tcp, CheckState.Passed));
            report(new ConnectionCheck(ConnectionStage.Tls, CheckState.Passed));
            return Task.CompletedTask;
        }
    }

    private sealed class FailedTlsTransport : IDsmTransportProbe
    {
        public Task ProbeAsync(Uri endpoint, Action<ConnectionCheck> report, CancellationToken cancellationToken)
        {
            report(new ConnectionCheck(ConnectionStage.Dns, CheckState.Passed));
            report(new ConnectionCheck(ConnectionStage.Tcp, CheckState.Passed));
            throw new AuthenticationException("synthetic-secret-must-not-be-shown");
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Form);

    private sealed class ScriptedHandler(string[] responses) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests
        {
            get;
        } = [];

        public Action<int>? BeforeResponse
        {
            get;
            init;
        }

        public HttpStatusCode ResponseStatus
        {
            get;
            init;
        } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var form = body.Split('&').Select(parameter => parameter.Split('=', 2))
                .ToDictionary(parts => WebUtility.UrlDecode(parts[0]), parts => WebUtility.UrlDecode(parts[1]));
            int index = Requests.Count;
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, form));
            BeforeResponse?.Invoke(index);
            return new HttpResponseMessage(ResponseStatus) { Content = new StringContent(responses[index]) };
        }
    }
}

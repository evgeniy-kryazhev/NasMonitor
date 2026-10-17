using System.Net;
using System.Text;
using System.Text.Json;
using NasMonitor.Core;
using NasMonitor.Providers.Synology;
using Xunit;

namespace NasMonitor.App.Tests;

public sealed class DsmSessionTests
{
    private static readonly NasProfile Profile = NasProfile.Create("Home", "https://nas.test:5001/proxy/", "reader");
    private const string ApiMap = """
        {"success":true,"data":{
        "SYNO.API.Auth":{"path":"auth.cgi","minVersion":1,"maxVersion":7},
        "SYNO.Core.System.Utilization":{"path":"entry.cgi","minVersion":1,"maxVersion":1},
        "SYNO.Storage.CGI.Storage":{"path":"entry.cgi","minVersion":1,"maxVersion":1}}}
        """;
    private const string Utilization = """
        {"success":true,"data":{"cpu":{"user_load":12,"system_load":8},"memory":{"real_usage":"42"},
        "network":[{"device":"eth0","rx":2048,"tx":1024}]}}
        """;
    private const string Storage = """
        {"success":true,"data":{"volumes":[{"id":"volume_1","status":"normal","size":{"used":"1024","total":"4096"}}],
        "disks":[{"name":"Drive 1","model":"Test drive","status":"normal","temp":35}]}}
        """;

    [Fact]
    public async Task DiscoveryLoginAndMetricsKeepSecretsOutOfUrlsAndPreserveProxyPath()
    {
        var handler = new FakeDsm();
        await using var session = Session(handler, "123456");
        var snapshot = await session.ReadAsync(default);
        Assert.Equal(20, snapshot.CpuPercent);
        Assert.Equal(42, snapshot.RamPercent);
        Assert.Equal(2048, snapshot.Network.Single().ReceiveBytesPerSecond);
        Assert.Equal(1024, snapshot.Volumes.Single().UsedBytes);
        Assert.Equal(35, snapshot.Disks.Single().Temperature);
        Assert.Empty(snapshot.Warnings);
        Assert.All(handler.Requests, r =>
        {
            Assert.StartsWith("/proxy/webapi/", r.Path);
            Assert.Equal("", r.Query);
            Assert.Equal(HttpMethod.Post, r.Method);
        });
        var login = handler.Requests.Single(r => r.Form["method"] == "login");
        Assert.Equal("test-password", login.Form["passwd"]);
        Assert.Equal("123456", login.Form["otp_code"]);
        Assert.All(handler.Requests.Where(r => r.Form["api"].StartsWith("SYNO.Core") || r.Form["api"].StartsWith("SYNO.Storage")),
            r => Assert.Equal("test-sid", r.Form["_sid"]));
    }

    [Fact]
    public async Task ExpiredSessionIsReauthenticatedOnceWithoutReusingOtp()
    {
        var handler = new FakeDsm();
        var utilizationCalls = 0;
        handler.Override = form => form["api"] == "SYNO.Core.System.Utilization" && utilizationCalls++ == 0
            ? "{\"success\":false,\"error\":{\"code\":106}}" : null;
        await using var session = Session(handler, "123456");
        await session.ReadAsync(default);
        var logins = handler.Requests.Where(r => r.Form["method"] == "login").ToArray();
        Assert.Equal(2, logins.Length);
        Assert.False(logins[1].Form.ContainsKey("otp_code"));
    }

    [Fact]
    public async Task AuthenticationFailureContainsNoPasswordAndDoesNotReadMetrics()
    {
        var handler = new FakeDsm { Override = form => form["method"] == "login" ? "{\"success\":false,\"error\":{\"code\":403}}" : null };
        await using var session = Session(handler);
        var error = await Assert.ThrowsAsync<NasException>(() => session.ReadAsync(default));
        Assert.Equal(NasErrorKind.Authentication, error.Kind);
        Assert.DoesNotContain("test-password", error.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task StoragePermissionDenialStillReturnsCpuAndPartialWarning()
    {
        var handler = new FakeDsm { Override = form => form["api"] == "SYNO.Storage.CGI.Storage" ? "{\"success\":false,\"error\":{\"code\":105}}" : null };
        await using var session = Session(handler);
        var snapshot = await session.ReadAsync(default);
        Assert.Equal(20, snapshot.CpuPercent);
        Assert.Empty(snapshot.Volumes);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("Permission denied"));
    }

    [Fact]
    public async Task UnsafeDiscoveredPathIsRejectedBeforeCredentialsAreSent()
    {
        var handler = new FakeDsm { Override = form => form["api"] == "SYNO.API.Info" ? ApiMap.Replace("auth.cgi", "https://other.test/auth.cgi") : null };
        await using var session = Session(handler);
        var error = await Assert.ThrowsAsync<NasException>(() => session.ReadAsync(default));
        Assert.Equal(NasErrorKind.Protocol, error.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MissingAuthDataIsAProtocolErrorRatherThanUnhandledParserFailure()
    {
        var handler = new FakeDsm { Override = form => form["method"] == "login" ? "{\"success\":true}" : null };
        await using var session = Session(handler);
        var error = await Assert.ThrowsAsync<NasException>(() => session.ReadAsync(default));
        Assert.Equal(NasErrorKind.Protocol, error.Kind);
    }

    [Fact]
    public void MissingMetricsStayUnknownInsteadOfZero()
    {
        using var json = JsonDocument.Parse("{}");
        var snapshot = DsmMetrics.Parse(json.RootElement, default, default, []);
        Assert.Null(snapshot.CpuPercent);
        Assert.Null(snapshot.RamPercent);
        Assert.Empty(snapshot.Network);
        Assert.NotEmpty(snapshot.Warnings);
    }

    [Theory]
    [InlineData("http://nas.test:5000/")]
    [InlineData("https://user:password@nas.test/")]
    [InlineData("https://nas.test/?token=value")]
    public void ProfileRejectsUnsafeEndpoint(string endpoint) =>
        Assert.Throws<ArgumentException>(() => NasProfile.Create("NAS", endpoint, "reader"));

    private static DsmSession Session(FakeDsm handler, string? otp = null) =>
        new(Profile, new("reader", "test-password"), otp, new HttpClient(handler));
    private sealed record Request(string Path, string Query, HttpMethod Method, Dictionary<string, string> Form);
    private sealed class FakeDsm : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];
        public Func<Dictionary<string, string>, string?>? Override;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var content = await request.Content!.ReadAsStringAsync(token);
            var form = content.Split('&').Select(item => item.Split('=', 2)).ToDictionary(
                pair => Decode(pair[0]), pair => Decode(pair[1]));
            Requests.Add(new(request.RequestUri!.AbsolutePath, request.RequestUri.Query, request.Method, form));
            var json = Override?.Invoke(form) ?? (form["api"] switch
            {
                "SYNO.API.Info" => ApiMap,
                "SYNO.API.Auth" => "{\"success\":true,\"data\":{\"sid\":\"test-sid\"}}",
                "SYNO.Core.System.Utilization" => Utilization,
                "SYNO.Storage.CGI.Storage" => Storage,
                _ => throw new InvalidOperationException("Unexpected test API")
            });
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
        private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
    }
}

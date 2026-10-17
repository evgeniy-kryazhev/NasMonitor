using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.Core.Connections;
using NasMonitor.Core.Monitoring;
using NasMonitor.Core.Profiles;
using NasMonitor.Providers.Synology.Monitoring;

namespace NasMonitor.Connection.Tests.Monitoring;

[TestClass]
public sealed class DsmMetricsTests
{
    private const string AuthDiscovery =
        """{"success":true,"data":{"SYNO.API.Auth":{"path":"auth.cgi","minVersion":3,"maxVersion":7}}}""";

    private const string Discovery = """
                                     {"success":true,"data":{
                                       "SYNO.Core.System.Utilization":{"path":"entry.cgi","minVersion":1,"maxVersion":1},
                                       "SYNO.Core.System":{"path":"entry.cgi","minVersion":1,"maxVersion":3},
                                       "SYNO.Storage.CGI.Storage":{"path":"entry.cgi","minVersion":1,"maxVersion":1}}}
                                     """;

    private const string Utilization = """
                                       {"success":true,"data":{"cpu":{"user_load":7,"system_load":4,"other_load":1,"1min_load":98},
                                         "memory":{"real_usage":38},"network":[{"device":"total","rx":2400000,"tx":850000},
                                         {"device":"bond0","rx":2400000,"tx":850000},{"device":"eth0","rx":1200000,"tx":425000}]}}
                                       """;

    private const string SystemInfo =
        """{"success":true,"data":{"model":"DS1821+","firmware_ver":"DSM synthetic","sys_temp":39,"up_time":"336:00:00"}}""";

    private const string Storage = """
                                   {"success":true,"data":{"volumes":[{"id":"volume1","size":{"used":"80","total":"100"}},
                                     {"id":"volume2","size":{"used":"240","total":"400"}}]}}
                                   """;

    private const string InformationCenterStorage =
        """{"success":true,"data":{"vol_info":[{"name":"volume_1","used_size":"250","total_size":"1000"}]}}""";

    private static NasProfile Profile =>
        new(Guid.NewGuid(), "Fixture NAS", new Uri("https://nas.example:5001"), "fixture");

    [TestMethod]
    public async Task InformationCenterReadsVolumeWithoutCallingDeniedStorageManagerAndCachesIt()
    {
        using var handler = new DsmHandler
        {
            InformationCenterStorageResponse = InformationCenterStorage,
            StorageResponse = """{"success":false,"error":{"code":105}}""",
            RequireCsrfHeader = true
        };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);

        NasSnapshot first = await session.ReadAsync(CancellationToken.None);
        NasSnapshot cached = await session.ReadAsync(CancellationToken.None);

        Assert.AreEqual(25.0, first.Storage!.Value);
        Assert.AreEqual(MetricAvailability.Available, first.Storage.Availability);
        NasVolume volume = first.Volumes!.Single();
        Assert.AreEqual("volume_1", volume.Id);
        Assert.AreEqual(250.0, volume.UsedBytes);
        Assert.AreEqual(1000.0, volume.TotalBytes);
        Assert.AreEqual(first.Storage.CollectedAt, cached.Storage!.CollectedAt);
        Assert.AreEqual(12.0, first.Cpu.Value);
        Assert.AreEqual(39.0, first.TemperatureCelsius!.Value);
        Assert.IsFalse(handler.Requests.Any(request => request.Form["api"] == "SYNO.Storage.CGI.Storage"));
        CapturedRequest storageRequest = handler.Requests.Single(request => request.Form.GetValueOrDefault("type") == "storage");
        Assert.AreEqual("SYNO.Core.System", storageRequest.Form["api"]);
        Assert.AreEqual("info", storageRequest.Form["method"]);
        Assert.AreEqual("1", storageRequest.Form["version"]);
        Assert.AreEqual("fixture-sid", storageRequest.Form["_sid"]);
        Assert.AreEqual("fixture-token", storageRequest.SynoToken);
        Assert.AreEqual("", storageRequest.Uri.Query);

        await session.ReadAsync(CancellationToken.None, true);
        Assert.AreEqual(2, handler.Requests.Count(request => request.Form.GetValueOrDefault("type") == "storage"));
        Assert.AreEqual(1, handler.Requests.Count(request => request.Form["method"] == "login"));
    }

    [TestMethod]
    public async Task InformationCenterAggregatesWholeVolumesWithoutAveragingPercentages()
    {
        using var handler = new DsmHandler
        {
            InformationCenterStorageResponse = """
                {"success":true,"data":{"vol_info":[
                  {"name":"first","used_size":"80","total_size":"100"},
                  {"name":"second","used_size":240,"total_size":400}]}}
                """
        };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);

        NasSnapshot snapshot = await session.ReadAsync(CancellationToken.None);

        Assert.AreEqual(64.0, snapshot.Storage!.Value);
        Assert.AreEqual(2, snapshot.Volumes!.Count);
    }

    [DataTestMethod]
    [DataRow("{\"success\":false,\"error\":{\"code\":105}}")]
    [DataRow("{\"success\":false,\"error\":{\"code\":104}}")]
    [DataRow("{\"success\":true,\"data\":{\"vol_info\":[]}}")]
    [DataRow("{\"success\":true,\"data\":{\"vol_info\":{}}}")]
    public async Task UnavailableInformationCenterUsesLegacyStorageWithoutAnotherLogin(string informationCenterResponse)
    {
        using var handler = new DsmHandler { InformationCenterStorageResponse = informationCenterResponse };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);

        NasSnapshot snapshot = await session.ReadAsync(CancellationToken.None);

        Assert.AreEqual(64.0, snapshot.Storage!.Value);
        Assert.AreEqual(1, handler.Requests.Count(request => request.Form["method"] == "load_info"));
        Assert.AreEqual(1, handler.Requests.Count(request => request.Form["method"] == "login"));
    }

    [DataTestMethod]
    [DataRow("{\"success\":true,\"data\":{\"vol_info\":[{\"name\":\"valid\",\"used_size\":25,\"total_size\":100},{\"name\":\"invalid\",\"used_size\":5,\"total_size\":0}]}}")]
    [DataRow("{\"success\":true,\"data\":{\"vol_info\":[{\"name\":\"same\",\"used_size\":25,\"total_size\":100},{\"name\":\"same\",\"used_size\":5,\"total_size\":10}]}}")]
    [DataRow("{\"success\":true,\"data\":{\"vol_info\":[{\"name\":\"volume\",\"used_size\":110,\"total_size\":100}]}}")]
    public async Task InvalidInformationCenterDataIsNeverShownAsPartialUsage(string response)
    {
        using var handler = new DsmHandler
        {
            InformationCenterStorageResponse = response,
            StorageResponse = """{"success":false,"error":{"code":105}}"""
        };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);

        NasSnapshot snapshot = await session.ReadAsync(CancellationToken.None);

        Assert.AreEqual(MetricAvailability.Invalid, snapshot.Storage!.Availability);
        Assert.IsNull(snapshot.Storage.Value);
        Assert.AreEqual(12.0, snapshot.Cpu.Value);
    }

    [TestMethod]
    public async Task InformationCenterSessionExpiryDoesNotTriggerLegacyStorage()
    {
        using var handler = new DsmHandler
        {
            InformationCenterStorageResponse = """{"success":false,"error":{"code":119}}"""
        };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);

        NasProviderException failure = await Assert.ThrowsExceptionAsync<NasProviderException>(() =>
            session.ReadAsync(CancellationToken.None));

        Assert.AreEqual(ConnectionError.SessionExpired, failure.Error);
        Assert.AreEqual(119, failure.DsmCode);
        Assert.IsFalse(handler.Requests.Any(request => request.Form["method"] == "load_info"));
    }

    [TestMethod]
    public async Task MonitoringLoginOmitsApplicationSessionAndLogoutTargetsReturnedSid()
    {
        using var handler = new DsmHandler();
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));

        await using (INasMetricsSession session =
                     await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None))
        {
            CapturedRequest login = handler.Requests.Single(request => request.Form["method"] == "login");
            Assert.IsFalse(login.Form.ContainsKey("session"),
                "Login must not request an unregistered DSM application session.");
        }

        CapturedRequest logout = handler.Requests.Single(request => request.Form["method"] == "logout");
        Assert.IsFalse(logout.Form.ContainsKey("session"), "Logout must target only the returned SID.");
        Assert.AreEqual("fixture-sid", logout.Form["_sid"]);
        Assert.AreEqual("fixture-token", logout.SynoToken);
        Assert.IsFalse(logout.Form.ContainsKey("SynoToken"));
    }

    [TestMethod]
    public async Task CsrfProtectedMetricsAcceptTokenHeaderWithoutCredentialsInUrl()
    {
        using var handler = new DsmHandler { RequireCsrfHeader = true };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));

        await using (INasMetricsSession session =
                     await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None))
        {
            NasSnapshot snapshot = await session.ReadAsync(CancellationToken.None);
            Assert.AreEqual(12.0, snapshot.Cpu.Value);
            Assert.AreEqual(64.0, snapshot.Storage!.Value);
            Assert.AreEqual(39.0, snapshot.TemperatureCelsius!.Value);
        }

        Assert.IsTrue(handler.Requests.All(request => request.Uri.Query == ""));
        Assert.IsTrue(handler.Requests.All(request => !request.Form.ContainsKey("SynoToken")));
        Assert.IsTrue(handler.Requests.Where(request => request.Form.ContainsKey("_sid"))
            .All(request => request.SynoToken == "fixture-token"));
        Assert.IsTrue(handler.Requests.Where(request => !request.Form.ContainsKey("_sid"))
            .All(request => request.SynoToken is null));
    }

    [TestMethod]
    public async Task ManualRefreshRequeriesSystemAndStorageWithoutAnotherLogin()
    {
        using var handler = new DsmHandler();
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);
        await session.ReadAsync(CancellationToken.None);

        await session.ReadAsync(CancellationToken.None, true);

        Assert.AreEqual(2, handler.Requests.Count(request => request.Form["method"] == "info" && !request.Form.ContainsKey("type")));
        Assert.AreEqual(2, handler.Requests.Count(request => request.Form["method"] == "load_info"));
        Assert.AreEqual(1, handler.Requests.Count(request => request.Form["method"] == "login"));
    }

    [TestMethod]
    public async Task MalformedSystemPayloadDoesNotEraseResourceAndStorageReadings()
    {
        using var handler = new DsmHandler { SystemResponse = """{"success":true,"data":[]}""" };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);

        NasSnapshot snapshot = await session.ReadAsync(CancellationToken.None);

        Assert.AreEqual(12.0, snapshot.Cpu.Value);
        Assert.AreEqual(64.0, snapshot.Storage!.Value);
        Assert.IsNull(snapshot.TemperatureCelsius!.Value);
        Assert.IsNull(snapshot.UptimeSeconds!.Value);
    }

    [TestMethod]
    public async Task ReadsAllMetricsInExplicitUnitsAndReusesSessionAndSlowCache()
    {
        using var handler = new DsmHandler();
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        NasProfile profile = Profile;
        await using (INasMetricsSession session =
                     await factory.ConnectAsync(profile, "synthetic &+password", "123456", CancellationToken.None))
        {
            NasSnapshot first = await session.ReadAsync(CancellationToken.None);
            NasSnapshot second = await session.ReadAsync(CancellationToken.None);

            Assert.AreEqual(profile.Id, first.NasId);
            Assert.AreEqual(12.0, first.Cpu.Value);
            Assert.AreEqual(38.0, first.Ram.Value);
            Assert.AreEqual(64.0, first.Storage!.Value);
            Assert.AreEqual(2400000.0, first.DownloadBytesPerSecond!.Value);
            Assert.AreEqual(850000.0, first.UploadBytesPerSecond!.Value);
            Assert.AreEqual(39.0, first.TemperatureCelsius!.Value);
            Assert.AreEqual(14 * 86400.0, first.UptimeSeconds!.Value);
            Assert.AreEqual("DS1821+", first.Model);
            Assert.IsTrue(first.Metrics.All(metric => metric!.Availability == MetricAvailability.Available));
            Assert.AreEqual(first.Storage.CollectedAt, second.Storage!.CollectedAt);
            Assert.AreEqual(1, handler.Requests.Count(request => request.Form.GetValueOrDefault("method") == "login"));
            Assert.AreEqual(1, handler.Requests.Count(request => request.Form.GetValueOrDefault("method") == "info" && !request.Form.ContainsKey("type")));
            Assert.AreEqual(1,
                handler.Requests.Count(request => request.Form.GetValueOrDefault("method") == "load_info"));
            Assert.AreEqual("load_info",
                handler.Requests.Single(request => request.Form["method"] == "load_info").Form["action"]);
            Assert.AreEqual(2,
                handler.Requests.Count(request =>
                    request.Form.GetValueOrDefault("api") == "SYNO.Core.System.Utilization"));
            Assert.IsFalse(JsonSerializer.Serialize(first).Contains("fixture-sid", StringComparison.Ordinal));
        }

        Assert.AreEqual("logout", handler.Requests.Last().Form["method"]);
        Assert.IsTrue(handler.Requests.All(request => request.Method == HttpMethod.Post && request.Uri.Query == ""));
        CapturedRequest login = handler.Requests.Single(request => request.Form.GetValueOrDefault("method") == "login");
        Assert.AreEqual("/webapi/auth.cgi", login.Uri.AbsolutePath);
        Assert.AreEqual("synthetic &+password", login.Form["passwd"]);
        Assert.AreEqual("123456", login.Form["otp_code"]);
    }

    [TestMethod]
    public async Task ZeroIsValidWhileMissingAndOutOfRangeValuesRemainUnavailable()
    {
        using var handler = new DsmHandler
        {
            UtilizationResponse =
                """{"success":true,"data":{"cpu":{"user_load":0,"system_load":0,"other_load":0},"memory":{},"network":[{"device":"total","rx":-1,"tx":"0"}]}}""",
            SystemResponse = """{"success":true,"data":{"sys_temp":900,"up_time":"unknown"}}"""
        };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);

        NasSnapshot snapshot = await session.ReadAsync(CancellationToken.None);

        Assert.AreEqual(0.0, snapshot.Cpu.Value);
        Assert.AreEqual(MetricAvailability.Available, snapshot.Cpu.Availability);
        Assert.IsNull(snapshot.Ram.Value);
        Assert.AreEqual(MetricAvailability.Unavailable, snapshot.Ram.Availability);
        Assert.AreEqual(MetricAvailability.Invalid, snapshot.DownloadBytesPerSecond!.Availability);
        Assert.AreEqual(0.0, snapshot.UploadBytesPerSecond!.Value);
        Assert.AreEqual(MetricAvailability.Invalid, snapshot.TemperatureCelsius!.Availability);
        Assert.AreEqual(MetricAvailability.Invalid, snapshot.UptimeSeconds!.Availability);
    }

    [TestMethod]
    public async Task DeniedStorageDoesNotPreventCpuMemoryNetworkOrSystemReadings()
    {
        using var handler = new DsmHandler { StorageResponse = """{"success":false,"error":{"code":105}}""" };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);

        NasSnapshot snapshot = await session.ReadAsync(CancellationToken.None);

        Assert.AreEqual(12.0, snapshot.Cpu.Value);
        Assert.AreEqual(39.0, snapshot.TemperatureCelsius!.Value);
        Assert.IsNull(snapshot.Storage!.Value);
        Assert.AreEqual(MetricAvailability.PermissionDenied, snapshot.Storage.Availability);
        Assert.AreEqual(105, snapshot.Storage.DsmCode);
        Assert.IsNull(snapshot.Cpu.DsmCode);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SingleVolumeWithoutStoragePoolReportsItsUsedPercentage(bool nestedData)
    {
        const string volumeData = """{"volumes":[{"id":"volume1","size":{"used":"250","total":"1000"}}]}""";
        string storageData = nestedData ? "{\"data\":" + volumeData + "}" : volumeData;
        using var handler = new DsmHandler { StorageResponse = "{\"success\":true,\"data\":" + storageData + "}" };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);

        NasSnapshot snapshot = await session.ReadAsync(CancellationToken.None);

        Assert.AreEqual(MetricAvailability.Available, snapshot.Storage!.Availability);
        Assert.AreEqual(25.0, snapshot.Storage.Value);
        Assert.AreEqual(1, snapshot.Volumes!.Count);
        Assert.AreEqual(250.0, snapshot.Volumes[0].UsedBytes);
        Assert.AreEqual(1000.0, snapshot.Volumes[0].TotalBytes);
    }

    [TestMethod]
    public async Task DeniedUtilizationStillAllowsSystemAndStorageMetrics()
    {
        using var handler = new DsmHandler { UtilizationResponse = """{"success":false,"error":{"code":105}}""" };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);

        NasSnapshot snapshot = await session.ReadAsync(CancellationToken.None);

        Assert.AreEqual(MetricAvailability.PermissionDenied, snapshot.Cpu.Availability);
        Assert.AreEqual(64.0, snapshot.Storage!.Value);
        Assert.AreEqual(39.0, snapshot.TemperatureCelsius!.Value);
    }

    [TestMethod]
    public async Task MissingApisAreUnsupportedAndNoUnannouncedEndpointIsCalled()
    {
        using var handler = new DsmHandler { MetricsDiscovery = """{"success":true,"data":{}}""" };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);

        NasSnapshot snapshot = await session.ReadAsync(CancellationToken.None);

        Assert.IsTrue(snapshot.Metrics.All(metric => metric!.Availability == MetricAvailability.Unsupported));
        Assert.AreEqual(3, handler.Requests.Count);
    }

    [TestMethod]
    public async Task InvalidStorageVolumeDoesNotProducePlausiblePartialAggregate()
    {
        using var handler = new DsmHandler
        {
            StorageResponse =
                """{"success":true,"data":{"volumes":[{"id":"volume1","size":{"used":"80","total":"100"}},{"id":"volume2","size":{"used":"40","total":"0"}}]}}"""
        };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using INasMetricsSession session =
            await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None);

        NasSnapshot snapshot = await session.ReadAsync(CancellationToken.None);

        Assert.AreEqual(MetricAvailability.Invalid, snapshot.Storage!.Availability);
        Assert.IsNull(snapshot.Storage.Value);
    }

    [TestMethod]
    public async Task ExpiredSessionIsClassifiedAndStillLoggedOut()
    {
        using var handler = new DsmHandler { UtilizationResponse = """{"success":false,"error":{"code":119}}""" };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));
        await using (INasMetricsSession session =
                     await factory.ConnectAsync(Profile, "fixture-password", null, CancellationToken.None))
        {
            NasProviderException failure =
                await Assert.ThrowsExceptionAsync<NasProviderException>(() =>
                    session.ReadAsync(CancellationToken.None));
            Assert.AreEqual(ConnectionError.SessionExpired, failure.Error);
            Assert.AreEqual(119, failure.DsmCode);
        }

        Assert.AreEqual("logout", handler.Requests.Last().Form["method"]);
    }

    [TestMethod]
    public async Task UnsafeMetricsPathAfterLoginIsRejectedAndSessionClosed()
    {
        using var handler = new DsmHandler
        {
            MetricsDiscovery =
                """{"success":true,"data":{"SYNO.Core.System.Utilization":{"path":"https://other.example/steal","minVersion":1,"maxVersion":1}}}"""
        };
        var factory = new SynologyMetricsSessionFactory(() => new HttpClient(handler, false));

        NasProviderException failure = await Assert.ThrowsExceptionAsync<NasProviderException>(() =>
            factory.ConnectAsync(Profile,
                "fixture-password",
                null,
                CancellationToken.None));

        Assert.AreEqual(ConnectionError.InvalidResponse, failure.Error);
        Assert.AreEqual("logout", handler.Requests.Last().Form["method"]);
        Assert.IsTrue(handler.Requests.All(request => request.Uri.Host == "nas.example"));
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Form, string? SynoToken);

    private sealed class DsmHandler : HttpMessageHandler
    {
        internal bool RequireCsrfHeader { get; init; }

        internal List<CapturedRequest> Requests
        {
            get;
        } = [];

        internal string UtilizationResponse
        {
            get;
            init;
        } = Utilization;

        internal string SystemResponse
        {
            get;
            init;
        } = SystemInfo;

        internal string StorageResponse
        {
            get;
            init;
        } = Storage;

        internal string InformationCenterStorageResponse
        {
            get;
            init;
        } = """{"success":false,"error":{"code":104}}""";

        internal string MetricsDiscovery
        {
            get;
            init;
        } = Discovery;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var form = body.Split('&').Select(parameter => parameter.Split('=', 2))
                .ToDictionary(parts => WebUtility.UrlDecode(parts[0]), parts => WebUtility.UrlDecode(parts[1]));
            string? synoToken = request.Headers.TryGetValues("X-SYNO-TOKEN", out IEnumerable<string>? tokens)
                ? tokens.Single()
                : null;
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, form, synoToken));
            if (RequireCsrfHeader && form.ContainsKey("_sid") && synoToken != "fixture-token")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"success":false,"error":{"code":119}}""")
                };
            }

            string response = form["method"] switch
            {
                "query" => form["query"] == "SYNO.API.Auth" ? AuthDiscovery : MetricsDiscovery,
                "login" => """{"success":true,"data":{"sid":"fixture-sid","synotoken":"fixture-token"}}""",
                "get" => UtilizationResponse,
                "info" => form.GetValueOrDefault("type") == "storage" ? InformationCenterStorageResponse : SystemResponse,
                "load_info" => StorageResponse,
                "logout" => """{"success":true}""",
                _ => throw new InvalidOperationException("Unexpected fixture method.")
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}

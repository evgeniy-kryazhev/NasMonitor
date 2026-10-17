using System.Text.Json;
using NasMonitor.Core;
using static NasMonitor.Providers.Synology.DsmProtocol;

namespace NasMonitor.Providers.Synology;

/// <summary>Creates DSM resource sessions using a fresh factory-managed HTTP client.</summary>
public sealed class SynologyMetricsSessionFactory(Func<HttpClient> createClient) : INasMetricsSessionFactory
{
    /// <inheritdoc />
    public async Task<INasMetricsSession> ConnectAsync(NasProfile profile, string password, string? otp,
        CancellationToken cancellationToken)
    {
        profile.Validate();
        if (!profile.Enabled || string.IsNullOrEmpty(password))
        {
            throw new NasProviderException(profile.Enabled ? ConnectionError.MissingPassword : ConnectionError.Disabled);
        }

        HttpClient client = createClient();
        var protocol = new DsmProtocol(client);
        var session = new MetricsSession(client, protocol, profile);
        ConnectionStage stage = ConnectionStage.Discovery;
        try
        {
            using JsonDocument discovery = await DiscoverAsync(protocol, profile.Endpoint, "SYNO.API.Auth", null,
                cancellationToken).ConfigureAwait(false);
            session.AuthenticationUri = GetApiUri(discovery, profile.Endpoint, "SYNO.API.Auth", 6);

            stage = ConnectionStage.Authentication;
            var login = AuthParameters("login");
            login["account"] = profile.Username;
            login["passwd"] = password;
            login["format"] = "sid";
            login["enable_syno_token"] = "yes";
            if (!string.IsNullOrWhiteSpace(otp))
            {
                login["otp_code"] = otp;
            }
            try
            {
                using JsonDocument response = await protocol.SendAsync(session.AuthenticationUri, login,
                    cancellationToken).ConfigureAwait(false);
                JsonElement data = response.RootElement.GetProperty("data");
                session.SessionId = data.GetProperty("sid").GetString();
                if (string.IsNullOrWhiteSpace(session.SessionId))
                {
                    throw new NasProviderException(ConnectionError.InvalidResponse);
                }
                if (data.TryGetProperty("synotoken", out JsonElement token))
                {
                    session.SynoToken = token.GetString();
                }
            }
            finally
            {
                login.Clear();
            }

            stage = ConnectionStage.Capabilities;
            using JsonDocument metricsDiscovery = await DiscoverAsync(protocol, profile.Endpoint,
                $"{MetricsSession.UtilizationApi},{MetricsSession.SystemApi},{MetricsSession.StorageApi}",
                session, cancellationToken).ConfigureAwait(false);
            session.MetricsUri = GetOptionalApiUri(metricsDiscovery, profile.Endpoint, MetricsSession.UtilizationApi);
            session.SystemUri = GetOptionalApiUri(metricsDiscovery, profile.Endpoint, MetricsSession.SystemApi);
            session.StorageUri = GetOptionalApiUri(metricsDiscovery, profile.Endpoint, MetricsSession.StorageApi);
            return session;
        }
        catch (NasProviderException failure) when (failure.DsmCode is int code)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw new NasProviderException(MapError(code, stage), code);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<JsonDocument> DiscoverAsync(DsmProtocol protocol, Uri endpoint, string api,
        MetricsSession? session, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string>
        {
            ["api"] = "SYNO.API.Info",
            ["version"] = "1",
            ["method"] = "query",
            ["query"] = api
        };
        session?.AddCredentials(query);
        return await protocol.SendAsync(new Uri(endpoint, "webapi/entry.cgi"), query,
            cancellationToken).ConfigureAwait(false);
    }

    private static Uri GetApiUri(JsonDocument discovery, Uri endpoint, string api, int version)
    {
        JsonElement data = discovery.RootElement.GetProperty("data");
        if (!data.TryGetProperty(api, out JsonElement definition) ||
            definition.GetProperty("minVersion").GetInt32() > version ||
            definition.GetProperty("maxVersion").GetInt32() < version)
        {
            throw new NasProviderException(ConnectionError.UnsupportedApi);
        }
        return ResolveApiPath(endpoint, definition.GetProperty("path").GetString());
    }

    private static Uri? GetOptionalApiUri(JsonDocument discovery, Uri endpoint, string api)
    {
        JsonElement data = discovery.RootElement.GetProperty("data");
        if (!data.TryGetProperty(api, out JsonElement definition) ||
            definition.GetProperty("minVersion").GetInt32() > 1 || definition.GetProperty("maxVersion").GetInt32() < 1)
        {
            return null;
        }
        return ResolveApiPath(endpoint, definition.GetProperty("path").GetString());
    }

    private sealed class MetricsSession(HttpClient client, DsmProtocol protocol, NasProfile profile) : INasMetricsSession
    {
        internal const string UtilizationApi = "SYNO.Core.System.Utilization";
        internal const string SystemApi = "SYNO.Core.System";
        internal const string StorageApi = "SYNO.Storage.CGI.Storage";
        internal Uri? AuthenticationUri { get; set; }
        internal Uri? MetricsUri { get; set; }
        internal Uri? SystemUri { get; set; }
        internal Uri? StorageUri { get; set; }
        internal string? SessionId { get; set; }
        internal string? SynoToken { get; set; }
        private bool disposed;
        private DsmMetricGroup? systemData;
        private DsmMetricGroup? storageData;
        private DateTimeOffset nextSlowPoll;

        internal void AddCredentials(Dictionary<string, string> parameters)
        {
            if (SessionId is not null)
            {
                AddSession(parameters, SessionId, SynoToken);
            }
        }

        public async Task<NasSnapshot> ReadAsync(CancellationToken cancellationToken, bool forceRefresh = false)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            DsmMetricGroup utilization = await ReadGroupAsync(MetricsUri, UtilizationApi, "get",
                cancellationToken).ConfigureAwait(false);
            if (forceRefresh || DateTimeOffset.UtcNow >= nextSlowPoll)
            {
                systemData = await ReadGroupAsync(SystemUri, SystemApi, "info", cancellationToken).ConfigureAwait(false);
                storageData = await ReadGroupAsync(StorageUri, StorageApi, "load_info", cancellationToken).ConfigureAwait(false);
                nextSlowPoll = DateTimeOffset.UtcNow.AddSeconds(60);
            }
            return DsmSnapshotParser.Parse(profile.Id, utilization, systemData!, storageData!);
        }

        private async Task<DsmMetricGroup> ReadGroupAsync(Uri? uri, string api, string method,
            CancellationToken cancellationToken)
        {
            DateTimeOffset collectedAt = DateTimeOffset.UtcNow;
            if (uri is null)
            {
                return new(null, MetricAvailability.Unsupported, collectedAt);
            }
            var parameters = new Dictionary<string, string>
            {
                ["api"] = api,
                ["version"] = "1",
                ["method"] = method
            };
            if (api == StorageApi)
            {
                // Storage CGI dispatches by action as well as the standard Web API method.
                parameters["action"] = method;
            }
            AddCredentials(parameters);
            try
            {
                using JsonDocument response = await protocol.SendAsync(uri, parameters, cancellationToken).ConfigureAwait(false);
                JsonElement data = response.RootElement.GetProperty("data");
                // Some Storage CGI versions wrap their payload in a second data object.
                if (api == StorageApi && data.TryGetProperty("data", out JsonElement nested))
                {
                    data = nested;
                }
                return new(data.Clone(), MetricAvailability.Available, DateTimeOffset.UtcNow);
            }
            catch (NasProviderException failure)
            {
                ConnectionError error = failure.DsmCode is int code ? CommonError(code) : failure.Error;
                if (error == ConnectionError.SessionExpired)
                {
                    throw new NasProviderException(error, failure.DsmCode);
                }
                MetricAvailability availability = error switch
                {
                    ConnectionError.PermissionDenied => MetricAvailability.PermissionDenied,
                    ConnectionError.UnsupportedApi => MetricAvailability.Unsupported,
                    _ => MetricAvailability.Unavailable
                };
                return new(null, availability, collectedAt);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return new(null, MetricAvailability.Invalid, collectedAt);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            try
            {
                if (SessionId is not null && AuthenticationUri is not null)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var logout = AuthParameters("logout");
                    AddCredentials(logout);
                    using JsonDocument response = await protocol.SendAsync(AuthenticationUri, logout,
                        timeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or
                OperationCanceledException or NasProviderException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                // Logout is best effort with a separate deadline; never log a response or SID.
            }
            finally
            {
                SessionId = null;
                SynoToken = null;
                client.Dispose();
            }
        }
    }
}

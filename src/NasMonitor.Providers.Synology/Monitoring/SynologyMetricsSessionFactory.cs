using System.Text.Json;
using static NasMonitor.Providers.Synology.Protocol.DsmProtocol;
using NasMonitor.Core.Connections;
using NasMonitor.Core.Monitoring;
using NasMonitor.Core.Profiles;
using NasMonitor.Providers.Synology.Protocol;

namespace NasMonitor.Providers.Synology.Monitoring;

/// <summary>Creates DSM resource sessions using a fresh factory-managed HTTP client.</summary>
public sealed class SynologyMetricsSessionFactory(Func<HttpClient> createClient) : INasMetricsSessionFactory
{
    /// <inheritdoc />
    public async Task<INasMetricsSession> ConnectAsync(NasProfile profile,
        string password,
        string? otp,
        CancellationToken cancellationToken)
    {
        profile.Validate();
        if (!profile.Enabled || string.IsNullOrEmpty(password))
        {
            throw new NasProviderException(profile.Enabled
                ? ConnectionError.MissingPassword
                : ConnectionError.Disabled);
        }

        HttpClient client = createClient();
        var protocol = new DsmProtocol(client);
        var session = new SynologyMetricsSession(client, protocol, profile);
        ConnectionStage stage = ConnectionStage.Discovery;
        try
        {
            using JsonDocument discovery = await DiscoverAsync(protocol,
                profile.Endpoint,
                "SYNO.API.Auth",
                null,
                cancellationToken).ConfigureAwait(false);
            session.AuthenticationUri = GetApiUri(discovery, profile.Endpoint, "SYNO.API.Auth", 6);

            stage = ConnectionStage.Authentication;
            Dictionary<string, string> login = AuthParameters("login");
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
                using JsonDocument response = await protocol.SendAsync(session.AuthenticationUri,
                    login,
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
            using JsonDocument metricsDiscovery = await DiscoverAsync(protocol,
                profile.Endpoint,
                $"{SynologyMetricsSession.UtilizationApi},{SynologyMetricsSession.SystemApi},{SynologyMetricsSession.StorageApi}",
                session,
                cancellationToken).ConfigureAwait(false);
            session.MetricsUri =
                GetOptionalApiUri(metricsDiscovery, profile.Endpoint, SynologyMetricsSession.UtilizationApi);
            session.SystemUri = GetOptionalApiUri(metricsDiscovery, profile.Endpoint, SynologyMetricsSession.SystemApi);
            session.StorageUri =
                GetOptionalApiUri(metricsDiscovery, profile.Endpoint, SynologyMetricsSession.StorageApi);
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

    private static async Task<JsonDocument> DiscoverAsync(DsmProtocol protocol,
        Uri endpoint,
        string api,
        SynologyMetricsSession? session,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string>
        {
            ["api"] = "SYNO.API.Info", ["version"] = "1", ["method"] = "query", ["query"] = api
        };
        session?.AddCredentials(query);
        return await protocol.SendAsync(new Uri(endpoint, "webapi/entry.cgi"),
            query,
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
}

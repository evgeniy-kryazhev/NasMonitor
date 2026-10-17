using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.RegularExpressions;
using NasMonitor.Core.Connections;
using NasMonitor.Core.Profiles;
using NasMonitor.Providers.Synology.Protocol;
using NasMonitor.Providers.Synology.Transport;
using static NasMonitor.Providers.Synology.Protocol.DsmProtocol;

namespace NasMonitor.Providers.Synology.Connections;

/// <summary>DSM login probe based on discovery and the documented Auth v6 protocol.</summary>
public sealed partial class SynologyConnectionTester(HttpClient httpClient, IDsmTransportProbe transport)
    : INasConnectionTester
{
    private readonly DsmProtocol protocol = new(httpClient);

    /// <inheritdoc />
    public async Task<NasConnectionResult> TestAsync(NasProfile profile,
        string password,
        string? otp,
        IProgress<ConnectionCheck>? progress,
        CancellationToken cancellationToken)
    {
        profile.Validate();
        var checks = new List<ConnectionCheck>();
        var capabilities = new List<NasCapability>();
        ConnectionStage stage = ConnectionStage.Dns;
        bool authenticated = false;
        string? sessionId = null;
        string? synoToken = null;
        Uri? authenticationUri = null;

        void Report(ConnectionCheck check)
        {
            checks.Add(check);
            progress?.Report(check);
            stage = check.Stage switch
            {
                ConnectionStage.Dns => ConnectionStage.Tcp,
                ConnectionStage.Tcp => ConnectionStage.Tls,
                _ => check.Stage
            };
        }

        if (!profile.Enabled || string.IsNullOrEmpty(password))
        {
            Report(new ConnectionCheck(ConnectionStage.Authentication,
                CheckState.Failed,
                profile.Enabled ? ConnectionError.MissingPassword : ConnectionError.Disabled));
            return new NasConnectionResult(false, checks, capabilities);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await transport.ProbeAsync(profile.Endpoint, Report, timeout.Token).ConfigureAwait(false);
            stage = ConnectionStage.Discovery;
            Uri entry = new(profile.Endpoint, "webapi/entry.cgi");
            using JsonDocument discovery = await protocol.SendAsync(entry,
                new Dictionary<string, string>
                {
                    ["api"] = "SYNO.API.Info", ["version"] = "1", ["method"] = "query", ["query"] = "SYNO.API.Auth"
                },
                timeout.Token).ConfigureAwait(false);
            JsonElement discoveredApis = discovery.RootElement.GetProperty("data");
            if (!discoveredApis.TryGetProperty("SYNO.API.Auth", out JsonElement authentication))
            {
                throw new NasProviderException(ConnectionError.UnsupportedApi);
            }

            int minimumVersion = authentication.GetProperty("minVersion").GetInt32();
            int maximumVersion = authentication.GetProperty("maxVersion").GetInt32();
            if (minimumVersion > 6 || maximumVersion < 6)
            {
                throw new NasProviderException(ConnectionError.UnsupportedApi);
            }

            authenticationUri = ResolveApiPath(profile.Endpoint, authentication.GetProperty("path").GetString());
            Report(new ConnectionCheck(stage, CheckState.Passed));

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

            using JsonDocument response =
                await protocol.SendAsync(authenticationUri, login, timeout.Token).ConfigureAwait(false);
            JsonElement data = response.RootElement.GetProperty("data");
            sessionId = data.GetProperty("sid").GetString();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new NasProviderException(ConnectionError.InvalidResponse);
            }

            if (data.TryGetProperty("synotoken", out JsonElement token))
            {
                synoToken = token.GetString();
            }

            authenticated = true;
            Report(new ConnectionCheck(stage, CheckState.Passed));

            stage = ConnectionStage.Capabilities;
            var query = new Dictionary<string, string>
            {
                ["api"] = "SYNO.API.Info", ["version"] = "1", ["method"] = "query", ["query"] = "all"
            };
            AddSession(query, sessionId, synoToken);
            using JsonDocument apiList = await protocol.SendAsync(entry, query, timeout.Token).ConfigureAwait(false);
            foreach (JsonProperty api in apiList.RootElement.GetProperty("data").EnumerateObject())
            {
                if (!ApiNamePattern().IsMatch(api.Name))
                {
                    continue;
                }

                capabilities.Add(new NasCapability(api.Name,
                    api.Value.GetProperty("minVersion").GetInt32(),
                    api.Value.GetProperty("maxVersion").GetInt32()));
            }

            Report(new ConnectionCheck(stage, CheckState.Passed));
            // API discovery does not prove access to resource/storage metrics.
            Report(new ConnectionCheck(ConnectionStage.Permissions, CheckState.Unverified));
        }
        catch (OperationCanceledException)
        {
            Report(new ConnectionCheck(stage,
                CheckState.Failed,
                cancellationToken.IsCancellationRequested ? ConnectionError.Cancelled : ConnectionError.Timeout));
        }
        catch (NasProviderException failure)
        {
            ConnectionError error = failure.DsmCode.HasValue ? MapError(failure.DsmCode.Value, stage) : failure.Error;
            Report(new ConnectionCheck(stage, CheckState.Failed, error, failure.DsmCode));
        }
        catch (AuthenticationException)
        {
            Report(new ConnectionCheck(stage, CheckState.Failed, ConnectionError.Tls));
        }
        catch (SocketException)
        {
            Report(new ConnectionCheck(stage,
                CheckState.Failed,
                stage == ConnectionStage.Dns ? ConnectionError.Dns : ConnectionError.Network));
        }
        catch (HttpRequestException exception)
        {
            ConnectionError error = exception.HttpRequestError == HttpRequestError.SecureConnectionError
                ? ConnectionError.Tls
                : ConnectionError.Network;
            Report(new ConnectionCheck(stage, CheckState.Failed, error));
        }
        catch (IOException)
        {
            Report(new ConnectionCheck(stage,
                CheckState.Failed,
                stage == ConnectionStage.Tls ? ConnectionError.Tls : ConnectionError.Network));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
                                              InvalidOperationException or FormatException or OverflowException)
        {
            Report(new ConnectionCheck(stage, CheckState.Failed, ConnectionError.InvalidResponse));
        }
        finally
        {
            if (sessionId is not null && authenticationUri is not null)
            {
                using var logoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    Dictionary<string, string> logout = AuthParameters("logout");
                    AddSession(logout, sessionId, synoToken);
                    using JsonDocument response = await protocol
                        .SendAsync(authenticationUri, logout, logoutTimeout.Token).ConfigureAwait(false);
                    Report(new ConnectionCheck(ConnectionStage.Logout, CheckState.Passed));
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or
                                                      OperationCanceledException or NasProviderException
                                                      or JsonException or InvalidOperationException)
                {
                    // A failed logout is visible, but never exposes response bodies or SID.
                    Report(new ConnectionCheck(ConnectionStage.Logout, CheckState.Failed, ConnectionError.ApiError));
                }
            }
        }

        return new NasConnectionResult(authenticated, checks, capabilities);
    }

    [GeneratedRegex("^SYNO\\.[A-Za-z0-9_.]{1,120}$", RegexOptions.CultureInvariant)]
    private static partial Regex ApiNamePattern();
}

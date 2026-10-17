using System.Text.Json;
using NasMonitor.Core.Connections;
using NasMonitor.Core.Monitoring;
using NasMonitor.Core.Profiles;
using NasMonitor.Providers.Synology.Protocol;
using static NasMonitor.Providers.Synology.Protocol.DsmProtocol;

namespace NasMonitor.Providers.Synology.Monitoring;

/// <summary>Owns one authenticated DSM session and its HTTP client.</summary>
internal sealed class SynologyMetricsSession(HttpClient client, DsmProtocol protocol, NasProfile profile)
    : INasMetricsSession
{
    internal const string UtilizationApi = "SYNO.Core.System.Utilization";
    internal const string SystemApi = "SYNO.Core.System";
    internal const string StorageApi = "SYNO.Storage.CGI.Storage";

    private bool disposed;
    private DateTimeOffset nextSlowPoll;
    private DsmMetricGroup? storageData;
    private DsmMetricGroup? systemData;

    internal Uri? AuthenticationUri
    {
        get;
        set;
    }

    internal Uri? MetricsUri
    {
        get;
        set;
    }

    internal Uri? SystemUri
    {
        get;
        set;
    }

    internal Uri? StorageUri
    {
        get;
        set;
    }

    internal string? SessionId
    {
        get;
        set;
    }

    internal string? SynoToken
    {
        get;
        set;
    }

    public async Task<NasSnapshot> ReadAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        DsmMetricGroup utilization = await ReadGroupAsync(MetricsUri,
            UtilizationApi,
            "get",
            cancellationToken).ConfigureAwait(false);
        if (forceRefresh || DateTimeOffset.UtcNow >= nextSlowPoll)
        {
            systemData = await ReadGroupAsync(SystemUri, SystemApi, "info", cancellationToken).ConfigureAwait(false);
            storageData = await ReadStorageAsync(cancellationToken).ConfigureAwait(false);
            nextSlowPoll = DateTimeOffset.UtcNow.AddSeconds(60);
        }

        return DsmSnapshotParser.Parse(profile.Id, utilization, systemData!, storageData!);
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
                Dictionary<string, string> logout = AuthParameters("logout");
                AddCredentials(logout);
                using JsonDocument response = await protocol.SendAsync(AuthenticationUri,
                    logout,
                    timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
                                              OperationCanceledException or NasProviderException or JsonException
                                              or InvalidOperationException or KeyNotFoundException)
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

    internal void AddCredentials(Dictionary<string, string> parameters)
    {
        if (SessionId is not null)
        {
            AddSession(parameters, SessionId, SynoToken);
        }
    }

    private async Task<DsmMetricGroup> ReadStorageAsync(CancellationToken cancellationToken)
    {
        // Info Center can be delegated independently of Storage Manager administration.
        DsmMetricGroup informationCenter = await ReadGroupAsync(SystemUri,
            SystemApi,
            "info",
            cancellationToken,
            type: "storage").ConfigureAwait(false);
        (NasMetric primaryMetric, _) = DsmSnapshotParser.ReadVolumes(informationCenter);
        if (primaryMetric.Availability == MetricAvailability.Available)
        {
            return informationCenter;
        }

        DsmMetricGroup storageManager = await ReadGroupAsync(StorageUri,
            StorageApi,
            "load_info",
            cancellationToken).ConfigureAwait(false);
        (NasMetric fallbackMetric, _) = DsmSnapshotParser.ReadVolumes(storageManager);
        if (fallbackMetric.Availability == MetricAvailability.Available)
        {
            return storageManager;
        }

        // Preserve a concrete primary failure instead of replacing it with a missing fallback API.
        return primaryMetric.Availability is MetricAvailability.PermissionDenied or MetricAvailability.Invalid ||
               fallbackMetric.Availability == MetricAvailability.Unsupported
            ? informationCenter
            : storageManager;
    }

    private async Task<DsmMetricGroup> ReadGroupAsync(Uri? uri,
        string api,
        string method,
        CancellationToken cancellationToken,
        string? type = null)
    {
        DateTimeOffset collectedAt = DateTimeOffset.UtcNow;
        if (uri is null)
        {
            return new DsmMetricGroup(null, MetricAvailability.Unsupported, collectedAt);
        }

        var parameters = new Dictionary<string, string> { ["api"] = api, ["version"] = "1", ["method"] = method };
        if (type is not null)
        {
            parameters["type"] = type;
        }

        if (api == StorageApi)
        {
            // Storage CGI dispatches by action as well as the standard Web API method.
            parameters["action"] = method;
        }

        AddCredentials(parameters);
        try
        {
            using JsonDocument response =
                await protocol.SendAsync(uri, parameters, cancellationToken).ConfigureAwait(false);
            JsonElement data = response.RootElement.GetProperty("data");
            // Some Storage CGI versions wrap their payload in a second data object.
            if (api == StorageApi && data.TryGetProperty("data", out JsonElement nested))
            {
                data = nested;
            }

            return new DsmMetricGroup(data.Clone(), MetricAvailability.Available, DateTimeOffset.UtcNow);
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
            return new DsmMetricGroup(null, availability, collectedAt, failure.DsmCode);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
                                              or InvalidOperationException)
        {
            return new DsmMetricGroup(null, MetricAvailability.Invalid, collectedAt);
        }
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NasMonitor.Core;

namespace NasMonitor.Providers.Synology;

/// <summary>Read-only DSM adapter. Credentials and SID are POST bodies, never URL parameters.</summary>
public sealed partial class DsmSession : INasSession
{
    private const string Auth = "SYNO.API.Auth", Utilization = "SYNO.Core.System.Utilization";
    private readonly HttpClient _http;
    private readonly NasProfile _profile;
    private NasCredentials? _credentials;
    private string? _otp, _sid;
    private Dictionary<string, Api>? _apis;
    public DsmSession(NasProfile profile, NasCredentials credentials, string? otp = null, HttpClient? http = null)
    {
        _profile = NasProfile.Create(profile.Name, profile.Endpoint.AbsoluteUri, profile.Username, profile.RememberCredentials, profile.Id);
        _credentials = credentials;
        _otp = string.IsNullOrWhiteSpace(otp) ? null : otp.Trim();
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.MaxResponseContentBufferSize = 4 * 1024 * 1024;
    }
    public async Task<NasSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (_sid is null) await LoginAsync(cancellationToken).ConfigureAwait(false);
                return await ReadMetricsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (NasException e) when (e.Kind == NasErrorKind.SessionExpired && attempt == 0)
            {
                _sid = null; // One reauthentication attempt; OTP is not reused.
            }
        }
    }
    private async Task LoginAsync(CancellationToken token)
    {
        if (_apis is null)
        {
            var data = await PostAsync("query.cgi", new()
            {
                ["api"] = "SYNO.API.Info", ["version"] = "1", ["method"] = "query",
                ["query"] = string.Join(',', Auth, Utilization, "SYNO.Storage.CGI.Storage", "SYNO.Core.Storage.Volume", "SYNO.Core.Storage.Disk")
            }, token).ConfigureAwait(false);
            if (data.ValueKind != JsonValueKind.Object) throw new NasException(NasErrorKind.Protocol, "DSM API discovery returned no API map.");
            _apis = new();
            try
            {
            foreach (var property in data.EnumerateObject())
            {
                var path = property.Value.GetProperty("path").GetString() ?? "";
                if (!CgiPath().IsMatch(path)) throw new NasException(NasErrorKind.Protocol, "DSM advertised an unsafe API path.");
                _apis[property.Name] = new(path, property.Value.GetProperty("minVersion").GetInt32(), property.Value.GetProperty("maxVersion").GetInt32());
            }
            }
            catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                _apis = null;
                throw new NasException(NasErrorKind.Protocol, "DSM returned an invalid API map.");
            }
        }
        var credentials = _credentials ?? throw new ObjectDisposedException(nameof(DsmSession));
        var parameters = new Dictionary<string, string>
        {
            ["account"] = credentials.Username, ["passwd"] = credentials.Password,
            ["session"] = "NasMonitor", ["format"] = "sid"
        };
        if (_otp is not null) parameters["otp_code"] = _otp;
        var auth = await CallAsync(Auth, "login", 7, parameters, token).ConfigureAwait(false);
        _sid = auth.ValueKind == JsonValueKind.Object && auth.TryGetProperty("sid", out var sid)
            && sid.ValueKind == JsonValueKind.String ? sid.GetString() : null;
        if (string.IsNullOrEmpty(_sid)) throw new NasException(NasErrorKind.Protocol, "DSM did not return a session identifier.");
        _otp = null;
    }
    private async Task<NasSnapshot> ReadMetricsAsync(CancellationToken token)
    {
        var warnings = new List<string>();
        async Task<JsonElement> Optional(string api, string method)
        {
            try { return await CallAsync(api, method, 1, new(), token).ConfigureAwait(false); }
            catch (NasException e) when (e.Kind is NasErrorKind.Permission or NasErrorKind.Unsupported)
            { warnings.Add(e.Message); return default; }
        }
        var utilization = await Optional(Utilization, "get");
        JsonElement volumes, disks;
        if (_apis!.ContainsKey("SYNO.Storage.CGI.Storage"))
        {
            var storage = await Optional("SYNO.Storage.CGI.Storage", "load_info");
            volumes = DsmMetrics.Property(storage, "volumes");
            disks = DsmMetrics.Property(storage, "disks");
        }
        else
        {
            volumes = DsmMetrics.Property(await Optional("SYNO.Core.Storage.Volume", "list"), "volumes");
            disks = DsmMetrics.Property(await Optional("SYNO.Core.Storage.Disk", "list"), "disks");
        }
        return DsmMetrics.Parse(utilization, volumes, disks, warnings);
    }
    private Task<JsonElement> CallAsync(string name, string method, int supportedVersion, Dictionary<string, string> parameters, CancellationToken token)
    {
        if (_apis is null || !_apis.TryGetValue(name, out var api) || api.Min > supportedVersion)
            throw new NasException(NasErrorKind.Unsupported, $"{name} is unavailable or requires an unsupported version.");
        parameters["api"] = name;
        parameters["version"] = Math.Min(api.Max, supportedVersion).ToString(CultureInfo.InvariantCulture);
        parameters["method"] = method;
        if (_sid is not null) parameters["_sid"] = _sid;
        return PostAsync(api.Path, parameters, token);
    }
    private async Task<JsonElement> PostAsync(string path, Dictionary<string, string> values, CancellationToken token)
    {
        using var body = new FormUrlEncodedContent(values);
        using var response = await _http.PostAsync(new Uri(_profile.Endpoint, "webapi/" + path), body, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode(); // Redirects are rejected; TLS validation remains enabled.
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            var root = document.RootElement;
            if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            {
                var code = root.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var c) ? c.GetInt32() : -1;
                throw code switch
                {
                    106 or 107 => new NasException(NasErrorKind.SessionExpired, "DSM session expired. Reconnect to sign in again."),
                    105 or 400 or 401 or 402 or 403 or 404 or 406 when values["api"] == Auth => new NasException(NasErrorKind.Authentication, $"DSM sign-in failed (code {code}). Check credentials and, if required, enter a fresh one-time code."),
                    105 => new NasException(NasErrorKind.Permission, $"Permission denied for {values["api"]}."),
                    102 or 103 or 104 => new NasException(NasErrorKind.Unsupported, $"{values["api"]} is not supported by this DSM version."),
                    _ => new NasException(NasErrorKind.Protocol, $"DSM request failed (code {code}).")
                };
            }
            return root.TryGetProperty("data", out var data) ? data.Clone() : default;
        }
        catch (JsonException) { throw new NasException(NasErrorKind.Protocol, "DSM returned an invalid JSON response."); }
        catch (Exception e) when (e is InvalidOperationException or FormatException or KeyNotFoundException)
        { throw new NasException(NasErrorKind.Protocol, "DSM returned an unexpected response format."); }
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_sid is not null && _apis is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await CallAsync(Auth, "logout", 7, new() { ["session"] = "NasMonitor" }, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or NasException) { }
        finally { _sid = null; _otp = null; _credentials = null; _http.Dispose(); }
    }
    private sealed record Api(string Path, int Min, int Max);
    [GeneratedRegex(@"^[A-Za-z0-9_-]+\.cgi$", RegexOptions.CultureInvariant)] private static partial Regex CgiPath();
}

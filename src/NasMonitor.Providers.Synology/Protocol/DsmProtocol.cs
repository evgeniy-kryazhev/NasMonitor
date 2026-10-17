using System.Text.Json;
using NasMonitor.Core.Connections;

namespace NasMonitor.Providers.Synology.Protocol;

/// <summary>Shared bounded POST transport, discovery-path validation and safe DSM errors.</summary>
internal sealed class DsmProtocol(HttpClient httpClient)
{
    private const int MaximumResponseBytes = 1_048_576;

    internal async Task<JsonDocument> SendAsync(Uri uri,
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        if (parameters.TryGetValue("SynoToken", out string? synoToken) && !string.IsNullOrEmpty(synoToken))
        {
            // DSM checks CSRF before processing the form body. Keep tokens out of URLs
            // and shared client defaults so each request uses only its own session token.
            request.Headers.Add("X-SYNO-TOKEN", synoToken);
        }

        request.Content = new FormUrlEncodedContent(parameters.Where(parameter => parameter.Key != "SynoToken"));
        using HttpResponseMessage response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new NasProviderException(ConnectionError.Http);
        }

        await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken).ConfigureAwait(false);
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (!document.RootElement.GetProperty("success").GetBoolean())
            {
                int code = document.RootElement.GetProperty("error").GetProperty("code").GetInt32();
                throw new NasProviderException(ConnectionError.ApiError, code);
            }

            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    internal static Uri ResolveApiPath(Uri endpoint, string? path)
    {
        // Reject absolute URLs, traversal and encoded separators before a password is sent.
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains("..", StringComparison.Ordinal) ||
            path.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '/' or '_' or '-' or '.')))
        {
            throw new NasProviderException(ConnectionError.InvalidResponse);
        }

        var root = new Uri(endpoint, "webapi/");
        var resolved = new Uri(root, path);
        if (!root.IsBaseOf(resolved))
        {
            throw new NasProviderException(ConnectionError.InvalidResponse);
        }

        return resolved;
    }

    internal static Dictionary<string, string> AuthParameters(string method)
    {
        // session selects a DSM application; arbitrary client labels can be denied at login.
        // Use the documented general DSM login and target logout with the returned _sid.
        return new Dictionary<string, string>
        {
            ["api"] = "SYNO.API.Auth",
            ["version"] = "6",
            ["method"] = method
        };
    }

    internal static void AddSession(Dictionary<string, string> parameters, string sessionId, string? synoToken)
    {
        parameters["_sid"] = sessionId;
        if (!string.IsNullOrEmpty(synoToken))
        {
            parameters["SynoToken"] = synoToken;
        }
    }

    internal static ConnectionError MapError(int code, ConnectionStage stage)
    {
        if (stage == ConnectionStage.Authentication)
        {
            return code switch
            {
                400 => ConnectionError.InvalidCredentials,
                401 => ConnectionError.AccountDisabled,
                402 => ConnectionError.PermissionDenied,
                403 or 406 => ConnectionError.OtpRequired,
                404 => ConnectionError.InvalidOtp,
                407 => ConnectionError.BlockedIp,
                408 or 409 or 410 => ConnectionError.PasswordChangeRequired,
                _ => CommonError(code)
            };
        }

        return CommonError(code);
    }

    internal static ConnectionError CommonError(int code)
    {
        return code switch
        {
            102 or 103 or 104 => ConnectionError.UnsupportedApi,
            105 => ConnectionError.PermissionDenied,
            106 or 107 or 119 => ConnectionError.SessionExpired,
            _ => ConnectionError.ApiError
        };
    }
}

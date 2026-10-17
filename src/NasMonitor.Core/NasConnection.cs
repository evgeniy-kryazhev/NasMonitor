namespace NasMonitor.Core;

/// <summary>Non-secret connection configuration for one NAS.</summary>
public sealed record NasProfile(Guid Id, string Name, Uri Endpoint, string Username,
    bool Enabled = true, Guid? CredentialId = null, Uri? ExternalEndpoint = null)
{
    /// <summary>Reject ambiguous URLs and plaintext authentication before any network access.</summary>
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 100 ||
            string.IsNullOrWhiteSpace(Username) || Username.Length > 256)
        {
            throw new ArgumentException("profile.invalid");
        }
        ValidateEndpoint(Endpoint);
        if (ExternalEndpoint is not null)
        {
            ValidateEndpoint(ExternalEndpoint);
        }
    }

    /// <summary>Require an HTTPS origin without credentials, query, fragment or proxy subpath.</summary>
    public static void ValidateEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(endpoint.Host) || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment) ||
            endpoint.AbsolutePath != "/")
        {
            throw new ArgumentException("endpoint.https-origin-required");
        }
    }
}

/// <summary>OS-protected password storage; identifiers are opaque and have no NAS/user data.</summary>
public interface ISecretStore
{
    /// <summary>Persist a password under an opaque identifier.</summary>
    Task WriteAsync(Guid credentialId, string password, CancellationToken cancellationToken);
    /// <summary>Read a password, or null if the item no longer exists.</summary>
    Task<string?> ReadAsync(Guid credentialId, CancellationToken cancellationToken);
    /// <summary>Remove a password; a missing item is already deleted.</summary>
    Task DeleteAsync(Guid credentialId, CancellationToken cancellationToken);
}

/// <summary>Ordered stages of a connection check.</summary>
public enum ConnectionStage
{
    Dns,
    Tcp,
    Tls,
    Discovery,
    Authentication,
    Capabilities,
    Permissions,
    Logout
}

/// <summary>Evidence for a stage, including deliberately unverified permission checks.</summary>
public enum CheckState
{
    Passed,
    Failed,
    Unverified
}

/// <summary>Safe error classification; never includes raw responses or exception messages.</summary>
public enum ConnectionError
{
    None,
    Disabled,
    MissingPassword,
    Dns,
    Network,
    Timeout,
    Tls,
    Http,
    InvalidResponse,
    UnsupportedApi,
    InvalidCredentials,
    AccountDisabled,
    PermissionDenied,
    OtpRequired,
    InvalidOtp,
    BlockedIp,
    PasswordChangeRequired,
    SessionExpired,
    ApiError,
    Cancelled
}

/// <summary>One check result with a controlled error and optional DSM numeric code.</summary>
public sealed record ConnectionCheck(ConnectionStage Stage, CheckState State,
    ConnectionError Error = ConnectionError.None, int? DsmCode = null);

/// <summary>API availability announced by DSM discovery, not proven metric permission.</summary>
public sealed record NasCapability(string Api, int MinimumVersion, int MaximumVersion);

/// <summary>A login check has no durable session and never contains credentials.</summary>
public sealed record NasConnectionResult(bool Authenticated, IReadOnlyList<ConnectionCheck> Checks,
    IReadOnlyList<NasCapability> Capabilities);

/// <summary>Tests a connection with short-lived credentials and closes its temporary session.</summary>
public interface INasConnectionTester
{
    /// <summary>Check transport, discover APIs, log in once and log out without polling.</summary>
    Task<NasConnectionResult> TestAsync(NasProfile profile, string password, string? otp,
        IProgress<ConnectionCheck>? progress, CancellationToken cancellationToken);
}

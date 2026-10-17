namespace NasMonitor.Core.Connections;

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

using System.Collections.Immutable;

namespace NasMonitor.Core;

public sealed record NasProfile(Guid Id, string Name, Uri Endpoint, string Username, bool RememberCredentials)
{
    public static NasProfile Create(string name, string endpoint, string username, bool remember = false, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Enter a NAS name and username.");
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Use an HTTPS address without credentials, query or fragment, for example https://nas.local:5001/.");
        return new(id ?? Guid.NewGuid(), name.Trim(), new Uri(uri.AbsoluteUri.TrimEnd('/') + "/"), username.Trim(), remember);
    }
}
public sealed record NasCredentials(string Username, string Password)
{
    public override string ToString() => "NasCredentials [redacted]";
}
public sealed record NetworkMetric(string Device, double? ReceiveBytesPerSecond, double? TransmitBytesPerSecond);
public sealed record VolumeMetric(string Name, long? UsedBytes, long? TotalBytes, string Status);
public sealed record DiskMetric(string Name, string Model, string Status, double? Temperature);
public sealed record NasSnapshot(DateTimeOffset CapturedAt, double? CpuPercent, double? RamPercent,
    ImmutableArray<NetworkMetric> Network, ImmutableArray<VolumeMetric> Volumes,
    ImmutableArray<DiskMetric> Disks, ImmutableArray<string> Warnings);
public enum NasErrorKind { Authentication, SessionExpired, Permission, Unsupported, Protocol }
public sealed class NasException(NasErrorKind kind, string message) : Exception(message)
{
    public NasErrorKind Kind { get; } = kind;
}
public interface INasSession : IAsyncDisposable
{
    Task<NasSnapshot> ReadAsync(CancellationToken cancellationToken);
}
public enum ConnectionState { Connecting, Online, Degraded, Offline, AuthenticationRequired, Stopped }
public sealed record MonitorUpdate(NasProfile Profile, ConnectionState State, NasSnapshot? Snapshot, string Message)
{
    public bool IsStale => Snapshot is not null && State is ConnectionState.Offline or ConnectionState.AuthenticationRequired or ConnectionState.Stopped;
}

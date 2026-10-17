namespace NasMonitor.Core.Connections;

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

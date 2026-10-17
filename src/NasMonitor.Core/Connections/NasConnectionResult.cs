namespace NasMonitor.Core.Connections;

/// <summary>A login check has no durable session and never contains credentials.</summary>
public sealed record NasConnectionResult(
    bool Authenticated,
    IReadOnlyList<ConnectionCheck> Checks,
    IReadOnlyList<NasCapability> Capabilities);

namespace NasMonitor.Core.Connections;

/// <summary>API availability announced by DSM discovery, not proven metric permission.</summary>
public sealed record NasCapability(string Api, int MinimumVersion, int MaximumVersion);

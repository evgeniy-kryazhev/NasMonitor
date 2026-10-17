namespace NasMonitor.Core.Updates;

/// <summary>A staged executable whose bytes have already passed SHA-256 verification.</summary>
public sealed record DownloadedUpdate(string FilePath, string Sha256, Version Version);

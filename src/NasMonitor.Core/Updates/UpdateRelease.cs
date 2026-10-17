namespace NasMonitor.Core.Updates;

/// <summary>A stable release and the exact executable asset for the running platform.</summary>
public sealed record UpdateRelease(
    Version Version,
    Uri PageUri,
    string Notes,
    string AssetName,
    Uri DownloadUri,
    long Size,
    string? Sha256,
    Uri? ChecksumsUri);

namespace NasMonitor.Core.Updates;

/// <summary>Distinguishes an unavailable release feed from a successful up-to-date check.</summary>
public enum UpdateCheckStatus
{
    UpToDate,
    Available,
    NoReleases,
    MissingAsset
}

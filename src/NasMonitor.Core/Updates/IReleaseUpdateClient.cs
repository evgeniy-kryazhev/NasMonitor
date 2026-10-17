namespace NasMonitor.Core.Updates;

/// <summary>Queries stable releases and downloads a verified executable.</summary>
public interface IReleaseUpdateClient
{
    /// <summary>Find a newer stable version with a suitable, verifiable asset.</summary>
    public Task<UpdateCheckResult> CheckAsync(Version currentVersion,
        string assetName,
        CancellationToken cancellationToken);

    /// <summary>Download into a private staging directory and reject incomplete or mismatched bytes.</summary>
    public Task<DownloadedUpdate> DownloadAsync(UpdateRelease release,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}

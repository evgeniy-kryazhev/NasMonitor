namespace NasMonitor.Core.Updates;

/// <summary>Stages the platform-appropriate replacement before the application exits.</summary>
public interface IAppUpdateInstaller
{
    /// <summary>Whether this launch is a supported portable executable.</summary>
    public bool CanInstall
    {
        get;
    }

    /// <summary>Start a verified helper and wait for its readiness before closing the main process.</summary>
    public Task PrepareAsync(DownloadedUpdate update, CancellationToken cancellationToken);
}

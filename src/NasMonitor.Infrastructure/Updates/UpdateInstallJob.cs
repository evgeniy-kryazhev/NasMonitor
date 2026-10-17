namespace NasMonitor.Infrastructure.Updates;

/// <summary>Non-secret handoff from the main process to its staged replacement helper.</summary>
public sealed record UpdateInstallJob(
    int ParentId,
    long ParentStartTicks,
    string TargetPath,
    string OriginalSha256,
    string PayloadSha256,
    string Version);

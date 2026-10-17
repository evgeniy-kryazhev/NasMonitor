namespace NasMonitor.Core.Updates;

/// <summary>Result of a release check, without filesystem or UI dependencies.</summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateRelease? Release = null);

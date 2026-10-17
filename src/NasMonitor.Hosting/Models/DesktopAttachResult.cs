namespace NasMonitor.Hosting.Models;

/// <summary>API outcome, deliberately distinct from visual OS acceptance.</summary>
public sealed record DesktopAttachResult(bool Success, string Code);

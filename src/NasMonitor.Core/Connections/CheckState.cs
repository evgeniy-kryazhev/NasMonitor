namespace NasMonitor.Core.Connections;

/// <summary>Evidence for a stage, including deliberately unverified permission checks.</summary>
public enum CheckState
{
    Passed,
    Failed,
    Unverified
}

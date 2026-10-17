namespace NasMonitor.Core.Connections;

/// <summary>One check result with a controlled error and optional DSM numeric code.</summary>
public sealed record ConnectionCheck(
    ConnectionStage Stage,
    CheckState State,
    ConnectionError Error = ConnectionError.None,
    int? DsmCode = null);

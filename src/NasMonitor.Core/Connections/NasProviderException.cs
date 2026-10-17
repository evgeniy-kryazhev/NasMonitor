namespace NasMonitor.Core.Connections;

/// <summary>Classified provider failure containing no response, URL or secret.</summary>
public sealed class NasProviderException(ConnectionError error, int? dsmCode = null) : Exception("nas.provider-failed")
{
    /// <summary>Safe failure classification.</summary>
    public ConnectionError Error
    {
        get;
    } = error;

    /// <summary>Optional numeric API error.</summary>
    public int? DsmCode
    {
        get;
    } = dsmCode;
}

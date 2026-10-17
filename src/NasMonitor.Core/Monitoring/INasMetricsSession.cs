namespace NasMonitor.Core.Monitoring;

/// <summary>Authenticated provider session; implementation owns SID and cleanup.</summary>
public interface INasMetricsSession : IAsyncDisposable
{
    /// <summary>Collect one sample; never start a polling loop here.</summary>
    public Task<NasSnapshot> ReadAsync(CancellationToken cancellationToken, bool forceRefresh = false);
}

using System.Collections.Concurrent;

namespace NasMonitor.Core;

/// <summary>One sequential worker per NAS; all views consume the same cached snapshots.</summary>
public sealed class NasMonitorService : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, Worker> _workers = new();
    private readonly TimeSpan _interval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    public event Action<MonitorUpdate>? Updated;
    public NasMonitorService(TimeSpan? interval = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _interval = interval ?? TimeSpan.FromSeconds(5);
        if (_interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }
    public MonitorUpdate? Get(Guid id) => _workers.TryGetValue(id, out var worker) ? worker.Latest : null;
    public void Start(NasProfile profile, INasSession session)
    {
        var worker = new Worker(profile, session);
        if (!_workers.TryAdd(profile.Id, worker)) throw new InvalidOperationException("This NAS is already monitored.");
        worker.Completion = RunAsync(worker);
    }
    public async Task StopAsync(Guid id)
    {
        if (!_workers.TryGetValue(id, out var worker)) return;
        await worker.StopLock.WaitAsync();
        try
        {
            if (!_workers.TryGetValue(id, out var current) || !ReferenceEquals(current, worker)) return;
            await worker.Cancellation.CancelAsync();
            try { await worker.Completion; }
            finally
            {
                await worker.Session.DisposeAsync();
                _workers.TryRemove(id, out _);
                worker.Cancellation.Dispose();
            }
        }
        finally { worker.StopLock.Release(); }
    }
    private async Task RunAsync(Worker worker)
    {
        var token = worker.Cancellation.Token;
        var failures = 0;
        NasSnapshot? last = null;
        void Publish(ConnectionState state, string message)
        {
            worker.Latest = new(worker.Profile, state, last, message);
            Updated?.Invoke(worker.Latest);
        }
        Publish(ConnectionState.Connecting, "Connecting…");
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    last = await worker.Session.ReadAsync(token).ConfigureAwait(false);
                    failures = 0;
                    Publish(last.Warnings.Length == 0 ? ConnectionState.Online : ConnectionState.Degraded,
                        last.Warnings.Length == 0 ? "Online" : string.Join(" ", last.Warnings));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (NasException e) when (e.Kind is NasErrorKind.Authentication or NasErrorKind.SessionExpired)
                {
                    Publish(ConnectionState.AuthenticationRequired, e.Message);
                    return; // No repeated invalid login attempts or account lockouts.
                }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or NasException)
                {
                    failures = Math.Min(failures + 1, 6);
                    Publish(ConnectionState.Offline, e is NasException known ? known.Message : "Connection failed or timed out. Check the address, network and HTTPS certificate.");
                }
                var pause = failures == 0 ? _interval : TimeSpan.FromSeconds(Math.Min(120, _interval.TotalSeconds * Math.Pow(2, failures)));
                await _delay(pause, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (token.IsCancellationRequested) Publish(ConnectionState.Stopped, "Disconnected");
        }
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var id in _workers.Keys) await StopAsync(id);
    }
    private sealed class Worker(NasProfile profile, INasSession session)
    {
        public NasProfile Profile { get; } = profile;
        public INasSession Session { get; } = session;
        public CancellationTokenSource Cancellation { get; } = new();
        public SemaphoreSlim StopLock { get; } = new(1, 1);
        public Task Completion { get; set; } = Task.CompletedTask;
        public MonitorUpdate? Latest;
    }
}

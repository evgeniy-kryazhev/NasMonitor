using NasMonitor.Core;
using Xunit;

namespace NasMonitor.App.Tests;

public sealed class MonitoringServiceTests
{
    private static NasProfile Profile => NasProfile.Create("NAS", "https://nas.test:5001/", "reader");
    private static NasSnapshot Snapshot => new(DateTimeOffset.UtcNow, 0, 25, [], [], [], []);

    [Fact]
    public async Task OfflineAndAuthenticationErrorsRetainLastReadingAndStopInvalidLoginRetries()
    {
        var profile = Profile;
        var session = new ScriptedSession();
        var updates = new List<MonitorUpdate>();
        var pauses = new List<TimeSpan>();
        await using var monitor = new NasMonitorService(delay: (pause, _) => { pauses.Add(pause); return Task.CompletedTask; });
        monitor.Updated += updates.Add;
        monitor.Start(profile, session);
        Assert.Equal(3, session.Reads);
        Assert.Equal(ConnectionState.AuthenticationRequired, monitor.Get(profile.Id)!.State);
        Assert.True(monitor.Get(profile.Id)!.IsStale);
        Assert.Equal(0, monitor.Get(profile.Id)!.Snapshot!.CpuPercent); // Real zero is distinct from unknown.
        Assert.Contains(updates, update => update.State == ConnectionState.Offline && update.IsStale);
        Assert.Equal(TimeSpan.FromSeconds(5), pauses[0]);
        Assert.Equal(TimeSpan.FromSeconds(10), pauses[1]);
        await monitor.StopAsync(profile.Id);
        Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public async Task DuplicateStartDoesNotCreateSecondPollerAndStopCancelsInflightRead()
    {
        var profile = Profile;
        var session = new BlockingSession();
        await using var monitor = new NasMonitorService();
        monitor.Start(profile, session);
        await session.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(() => monitor.Start(profile, session));
        await monitor.StopAsync(profile.Id).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, session.Reads);
        Assert.True(session.Disposed);
        Assert.Null(monitor.Get(profile.Id));
    }

    private sealed class ScriptedSession : INasSession
    {
        public int Reads, Disposals;
        public Task<NasSnapshot> ReadAsync(CancellationToken token) => ++Reads switch
        {
            1 => Task.FromResult(Snapshot),
            2 => Task.FromException<NasSnapshot>(new HttpRequestException("Test outage")),
            _ => Task.FromException<NasSnapshot>(new NasException(NasErrorKind.Authentication, "Fresh credentials required"))
        };
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class BlockingSession : INasSession
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads;
        public bool Disposed;
        public async Task<NasSnapshot> ReadAsync(CancellationToken token)
        {
            Reads++;
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Snapshot;
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}

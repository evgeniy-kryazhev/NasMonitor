using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.App.ViewModels;
using NasMonitor.Core.Connections;
using NasMonitor.Core.Monitoring;
using NasMonitor.Core.Profiles;
using NasMonitor.Core.Security;
using NasMonitor.Infrastructure.Monitoring;
using NasMonitor.Infrastructure.Persistence;
using NasMonitor.Infrastructure.Profiles;

namespace NasMonitor.Connection.Tests.Monitoring;

[TestClass]
public sealed class MonitoringTests
{
    [TestMethod]
    public void SessionFailureDisplaysDsmCodeWithoutSessionCredentials()
    {
        var display = new WidgetViewModel();
        var state = new NasMonitorState(Guid.NewGuid(),
            NasMonitorStatus.AuthenticationRequired,
            Error: ConnectionError.SessionExpired,
            DsmCode: 119);

        display.ApplyMonitoringState(state, DateTimeOffset.UtcNow);

        StringAssert.Contains(display.MonitorDetail, "DSM 119");
        Assert.AreEqual("—", display.CpuText);
    }

    [TestMethod]
    public async Task SynchronizingUnchangedProfilesDoesNotCreateAnotherPoller()
    {
        await using var fixture = new MonitorFixture();
        NasProfile profile = await fixture.SaveAsync("NAS A");
        Task<NasMonitorState> firstPoll = WaitForStateAsync(fixture.Coordinator,
            profile.Id,
            state => state.Status == NasMonitorStatus.Online);

        await fixture.Coordinator.SynchronizeAsync([profile], CancellationToken.None);
        await firstPoll;
        await fixture.Coordinator.SynchronizeAsync([profile], CancellationToken.None);

        Assert.AreEqual(1, fixture.Factory.Calls.Count);
        Assert.AreEqual(1, fixture.Factory.Created.Single().ReadCount);
        Assert.AreEqual(NasMonitorStatus.Online, fixture.Coordinator.GetState(profile.Id)!.Status);
    }

    [TestMethod]
    public async Task OneUnavailableNasDoesNotBlockAnotherNasAndCacheIsMarkedStale()
    {
        await using var fixture = new MonitorFixture();
        NasProfile first = await fixture.SaveAsync("NAS A");
        NasProfile second = await fixture.SaveAsync("NAS B");
        fixture.Factory.Create = profile => new FakeSession(profile.Id,
            (count, _) =>
                profile.Id == first.Id && count > 1
                    ? Task.FromException<NasSnapshot>(new HttpRequestException("synthetic-sensitive-message"))
                    : Task.FromResult(Sample(profile.Id)));
        await fixture.Coordinator.ConnectAsync(first, first.Endpoint, null, null, CancellationToken.None);
        await fixture.Coordinator.ConnectAsync(second, second.Endpoint, null, null, CancellationToken.None);

        Task<NasMonitorState> offline = WaitForStateAsync(fixture.Coordinator,
            first.Id,
            state => state.Status == NasMonitorStatus.Offline);
        await fixture.Coordinator.RefreshAsync(first.Id, CancellationToken.None);
        NasMonitorState failed = await offline;

        Assert.IsTrue(failed.IsStale);
        Assert.IsNotNull(failed.LastSuccessAt);
        Assert.AreEqual(ConnectionError.Network, failed.Error);
        Assert.AreEqual(NasMonitorStatus.Online, fixture.Coordinator.GetState(second.Id)!.Status);
        var display = new WidgetViewModel();
        display.ApplyMonitoringState(failed, DateTimeOffset.UtcNow);
        Assert.AreEqual("—", display.CpuText);
        Assert.IsFalse(display.MonitorDetail.Contains("synthetic-sensitive-message", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SessionExpiryReconnectsOnceFromSecretStoreWithoutReusingOtp()
    {
        await using var fixture = new MonitorFixture();
        NasProfile profile = await fixture.SaveAsync("NAS A");
        int sessions = 0;
        fixture.Factory.Create = connected =>
        {
            int sessionNumber = Interlocked.Increment(ref sessions);
            return new FakeSession(connected.Id,
                (count, _) =>
                    sessionNumber == 1 && count > 1
                        ? Task.FromException<NasSnapshot>(new NasProviderException(ConnectionError.SessionExpired, 119))
                        : Task.FromResult(Sample(connected.Id, sessionNumber * 10)));
        };
        await fixture.Coordinator.ConnectAsync(profile,
            profile.Endpoint,
            "one-time-password",
            "123456",
            CancellationToken.None);
        Task<NasMonitorState> reconnected = WaitForStateAsync(fixture.Coordinator,
            profile.Id,
            state => state.Snapshot?.Cpu.Value == 20);

        await fixture.Coordinator.RefreshAsync(profile.Id, CancellationToken.None);
        await reconnected;

        Assert.AreEqual(2, fixture.Factory.Calls.Count);
        Assert.AreEqual("one-time-password", fixture.Factory.Calls.First().Password);
        Assert.AreEqual("fixture-saved-password", fixture.Factory.Calls.Last().Password);
        Assert.IsNull(fixture.Factory.Calls.Last().Otp);
        Assert.AreEqual(1, fixture.Factory.Created.First().DisposeCount);
    }

    [TestMethod]
    public async Task SessionExpiryWithoutRememberedPasswordRequiresManualLogin()
    {
        await using var fixture = new MonitorFixture();
        NasProfile profile = await fixture.SaveAsync("NAS A", false);
        fixture.Factory.Create = connected => new FakeSession(connected.Id,
            (count, _) => count == 1
                ? Task.FromResult(Sample(connected.Id))
                : Task.FromException<NasSnapshot>(new NasProviderException(ConnectionError.SessionExpired)));
        await fixture.Coordinator.ConnectAsync(profile,
            profile.Endpoint,
            "ephemeral-password",
            "123456",
            CancellationToken.None);
        Task<NasMonitorState> needsPassword = WaitForStateAsync(fixture.Coordinator,
            profile.Id,
            state => state.Error == ConnectionError.MissingPassword);

        await fixture.Coordinator.RefreshAsync(profile.Id, CancellationToken.None);
        await needsPassword;

        Assert.AreEqual(1, fixture.Factory.Calls.Count);
    }

    [TestMethod]
    public async Task InvalidCredentialsStopAutomaticAttempts()
    {
        await using var fixture = new MonitorFixture();
        NasProfile profile = await fixture.SaveAsync("NAS A");
        fixture.Factory.Failure = new NasProviderException(ConnectionError.InvalidCredentials, 400);

        NasMonitorState result =
            await fixture.Coordinator.ConnectAsync(profile, profile.Endpoint, null, null, CancellationToken.None);
        await fixture.Coordinator.SynchronizeAsync([profile], CancellationToken.None);

        Assert.AreEqual(NasMonitorStatus.AuthenticationRequired, result.Status);
        Assert.AreEqual(ConnectionError.InvalidCredentials, result.Error);
        Assert.AreEqual(1, fixture.Factory.Calls.Count);
    }

    [TestMethod]
    public async Task DisabledAndDeletedProfilesCloseSessionsAndNeverPollDisabledNas()
    {
        await using var fixture = new MonitorFixture();
        NasProfile profile = await fixture.SaveAsync("NAS A");
        await fixture.Coordinator.ConnectAsync(profile, profile.Endpoint, null, null, CancellationToken.None);

        await fixture.Coordinator.SynchronizeAsync([profile with { Enabled = false }], CancellationToken.None);
        await WaitForStateAsync(fixture.Coordinator, profile.Id, state => state.Status == NasMonitorStatus.Disabled);
        Assert.AreEqual(1, fixture.Factory.Created.Single().DisposeCount);
        Assert.AreEqual(1, fixture.Factory.Calls.Count);

        await fixture.Coordinator.SynchronizeAsync([], CancellationToken.None);
        Assert.IsNull(fixture.Coordinator.GetState(profile.Id));
    }

    [TestMethod]
    public async Task GlobalRequestConcurrencyIsBoundedAndCancellationDrainsAllSessions()
    {
        await using var fixture = new MonitorFixture();
        var configuredProfiles = new List<NasProfile>();
        for (int index = 0; index < 5; index++)
        {
            configuredProfiles.Add(await fixture.SaveAsync("NAS " + index));
        }

        int active = 0;
        var threeActive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Factory.Create = connected => new FakeSession(connected.Id,
            async (_, token) =>
            {
                int concurrent = Interlocked.Increment(ref active);
                if (concurrent == 3)
                {
                    threeActive.TrySetResult();
                }

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return Sample(connected.Id);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });

        await fixture.Coordinator.SynchronizeAsync(configuredProfiles, CancellationToken.None);
        await threeActive.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(3, active);
        Assert.AreEqual(3, fixture.Factory.Calls.Count);

        await fixture.Coordinator.DisposeAsync();
        Assert.AreEqual(0, active);
        Assert.IsTrue(fixture.Factory.Created.All(session => session.DisposeCount == 1));
    }

    [TestMethod]
    public async Task ManualRefreshRecoversTransientFailureAndExternalAddressIsExplicit()
    {
        await using var fixture = new MonitorFixture();
        NasProfile profile = await fixture.SaveAsync("NAS A");
        profile = profile with { ExternalEndpoint = new Uri("https://external.example:5001") };
        profile = await fixture.Profiles.SaveAsync(profile, "fixture-saved-password", true, CancellationToken.None);
        fixture.Factory.Create = connected => new FakeSession(connected.Id,
            (count, _) => count == 1
                ? Task.FromException<NasSnapshot>(new HttpRequestException())
                : Task.FromResult(Sample(connected.Id)));
        NasMonitorState offline = await fixture.Coordinator.ConnectAsync(profile,
            profile.ExternalEndpoint!,
            null,
            null,
            CancellationToken.None);
        Assert.AreEqual(NasMonitorStatus.Offline, offline.Status);
        Task<NasMonitorState> online = WaitForStateAsync(fixture.Coordinator,
            profile.Id,
            state => state.Status == NasMonitorStatus.Online);

        await fixture.Coordinator.RefreshAsync(profile.Id, CancellationToken.None);
        await online;

        Assert.AreEqual(profile.ExternalEndpoint, fixture.Factory.Calls.Single().Profile.Endpoint);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => fixture.Coordinator.ConnectAsync(profile,
            new Uri("https://unexpected.example"),
            null,
            null,
            CancellationToken.None));
    }

    [TestMethod]
    public void DisplayDistinguishesZeroMissingDeniedAndAgedMetrics()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        NasSnapshot snapshot = Sample(Guid.NewGuid(), 0) with
        {
            Ram = NasMetric.Create(null, now),
            Storage = new NasMetric(null, now, MetricAvailability.PermissionDenied) { DsmCode = 105 },
            TemperatureCelsius = NasMetric.Create(39, now.AddMinutes(-5), -50, 150),
            DownloadBytesPerSecond = NasMetric.Create(2400000, now, maximum: double.MaxValue)
        };
        var state = new NasMonitorState(snapshot.NasId, NasMonitorStatus.Degraded, snapshot, now, now);
        var display = new WidgetViewModel();

        display.ApplyMonitoringState(state, now);

        Assert.AreEqual("0%", display.CpuText);
        Assert.AreEqual("—", display.RamText);
        Assert.AreEqual("—", display.StorageText);
        Assert.AreEqual("—", display.TemperatureText);
        Assert.IsTrue(display.StorageDetail.Contains("прав", StringComparison.Ordinal));
        StringAssert.Contains(display.StorageDetail, "DSM 105");
        StringAssert.Contains(display.MonitorDetail, "Хранилище");
        StringAssert.Contains(display.MonitorDetail, "DSM 105");
        Assert.IsTrue(display.HasMonitorDetail);
        Assert.IsTrue(display.TemperatureDetail.Contains("устарели", StringComparison.Ordinal));
        Assert.IsTrue(display.DownloadText.EndsWith(" MB/s", StringComparison.Ordinal));
        display.ApplyMonitoringState(state, now.AddSeconds(40));
        Assert.AreEqual("—", display.CpuText);
        Assert.AreEqual("Устарело", display.MonitorStatus);
    }

    private static NasSnapshot Sample(Guid id, double cpu = 12)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var metric = NasMetric.Create(cpu, now);
        return new NasSnapshot(id, now, metric, metric, metric, metric, metric, metric, metric);
    }

    private static async Task<NasMonitorState> WaitForStateAsync(NasMonitoringCoordinator monitoring,
        Guid id,
        Func<NasMonitorState, bool> predicate)
    {
        var completion = new TaskCompletionSource<NasMonitorState>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Changed(NasMonitorState state)
        {
            if (state.NasId == id && predicate(state))
            {
                completion.TrySetResult(state);
            }
        }

        monitoring.StateChanged += Changed;
        try
        {
            if (monitoring.GetState(id) is { } current)
            {
                Changed(current);
            }

            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            monitoring.StateChanged -= Changed;
        }
    }

    private sealed class MonitorFixture : IAsyncDisposable
    {
        private readonly string directory =
            Path.Combine(Path.GetTempPath(), "NasMonitor-monitor-tests-" + Guid.NewGuid());

        private readonly NasProfileRepository repository;

        internal MonitorFixture()
        {
            repository = new NasProfileRepository(Path.Combine(directory, "profiles.json"));
            Profiles = new NasProfileService(repository, new MemorySecrets());
            Coordinator = new NasMonitoringCoordinator(Factory, Profiles, TimeProvider.System);
        }

        internal NasProfileService Profiles
        {
            get;
        }

        internal FakeFactory Factory
        {
            get;
        } = new();

        internal NasMonitoringCoordinator Coordinator
        {
            get;
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
            repository.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        internal Task<NasProfile> SaveAsync(string name, bool rememberPassword = true)
        {
            var profile = new NasProfile(Guid.NewGuid(), name, new Uri("https://nas.example:5001"), "fixture");
            return Profiles.SaveAsync(profile, "fixture-saved-password", rememberPassword, CancellationToken.None);
        }
    }

    private sealed class MemorySecrets : ISecretStore
    {
        private readonly ConcurrentDictionary<Guid, string> items = new();

        public Task WriteAsync(Guid id, string password, CancellationToken cancellationToken)
        {
            items[id] = password;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(items.GetValueOrDefault(id));
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            items.TryRemove(id, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFactory : INasMetricsSessionFactory
    {
        internal ConcurrentQueue<(NasProfile Profile, string Password, string? Otp)> Calls
        {
            get;
        } = new();

        internal ConcurrentQueue<FakeSession> Created
        {
            get;
        } = new();

        internal Func<NasProfile, FakeSession> Create
        {
            get;
            set;
        } = profile => new FakeSession(profile.Id);

        internal Exception? Failure
        {
            get;
            set;
        }

        public Task<INasMetricsSession> ConnectAsync(NasProfile profile,
            string password,
            string? otp,
            CancellationToken cancellationToken)
        {
            Calls.Enqueue((profile, password, otp));
            if (Failure is not null)
            {
                return Task.FromException<INasMetricsSession>(Failure);
            }

            FakeSession session = Create(profile);
            Created.Enqueue(session);
            return Task.FromResult<INasMetricsSession>(session);
        }
    }

    private sealed class FakeSession(Guid id, Func<int, CancellationToken, Task<NasSnapshot>>? read = null)
        : INasMetricsSession
    {
        internal int DisposeCount;
        internal int ReadCount;

        public Task<NasSnapshot> ReadAsync(CancellationToken cancellationToken, bool forceRefresh = false)
        {
            int count = Interlocked.Increment(ref ReadCount);
            return read?.Invoke(count, cancellationToken) ?? Task.FromResult(Sample(id));
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref DisposeCount);
            return ValueTask.CompletedTask;
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.App.Persistence;
using NasMonitor.App.Services;
using NasMonitor.App.ViewModels;
using NasMonitor.Core.Monitoring;
using NasMonitor.Core.Profiles;
using NasMonitor.Core.Security;
using NasMonitor.Infrastructure.Monitoring;
using NasMonitor.Infrastructure.Persistence;
using NasMonitor.Infrastructure.Profiles;

namespace NasMonitor.Connection.Tests.Monitoring;

[TestClass]
public sealed class MultipleWidgetTests
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "NasMonitor.Tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        string testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NasMonitor.Tests"));
        string resolved = Path.GetFullPath(directory);
        Assert.AreEqual(testRoot, Path.GetDirectoryName(resolved));
        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, true);
        }
    }

    [TestMethod]
    public async Task WidgetsShareNasSessionsButKeepDifferentSourcesAndPersistence()
    {
        using var repository = new NasProfileRepository(Path.Combine(directory, "profiles.json"));
        var profiles = new NasProfileService(repository, new EmptySecrets());
        var factory = new CountingSessions();
        await using var coordinator = new NasMonitoringCoordinator(factory, profiles, TimeProvider.System);
        using var presenter = new WidgetMonitoringPresenter(coordinator);
        var firstNas = new NasProfile(Guid.NewGuid(), "Primary", new Uri("https://primary.example:5001"), "fixture");
        var secondNas = new NasProfile(Guid.NewGuid(), "Backup", new Uri("https://backup.example:5001"), "fixture");
        await coordinator.ConnectAsync(firstNas, firstNas.Endpoint, "fixture", null, CancellationToken.None);
        await coordinator.ConnectAsync(secondNas, secondNas.Endpoint, "fixture", null, CancellationToken.None);
        var storage = new WidgetSettingsStore(directory);
        WidgetSettingsStore firstStorage = storage.ForWidget(Guid.NewGuid());
        WidgetSettingsStore secondStorage = storage.ForWidget(Guid.NewGuid());
        var first = new WidgetViewModel();
        var second = new WidgetViewModel();
        await presenter.RegisterAsync(first, firstStorage, CancellationToken.None);
        await presenter.RegisterAsync(second, secondStorage, CancellationToken.None);
        await presenter.SynchronizeAsync([firstNas, secondNas], CancellationToken.None);

        first.SelectedNas = firstNas;
        second.SelectedNas = secondNas;
        await presenter.FlushAsync(first, CancellationToken.None);
        await presenter.FlushAsync(second, CancellationToken.None);
        Assert.AreEqual("10%", first.CpuText);
        Assert.AreEqual("20%", second.CpuText);
        Assert.AreEqual(firstNas.Id, await firstStorage.LoadNasSelectionAsync(CancellationToken.None));
        Assert.AreEqual(secondNas.Id, await secondStorage.LoadNasSelectionAsync(CancellationToken.None));

        second.SelectedNas = firstNas;
        await presenter.FlushAsync(second, CancellationToken.None);
        presenter.Refresh();
        Assert.AreEqual(first.CpuText, second.CpuText);
        Assert.AreEqual(2, factory.ConnectCount);

        first.SelectedNas = secondNas;
        first.SelectedNas = firstNas;
        first.SelectedNas = secondNas;
        await presenter.FlushAsync(first, CancellationToken.None);
        Assert.AreEqual(secondNas.Id, await firstStorage.LoadNasSelectionAsync(CancellationToken.None));
        Assert.AreEqual(firstNas.Id, await secondStorage.LoadNasSelectionAsync(CancellationToken.None));

        presenter.Unregister(first);
        presenter.Refresh(second);
        Assert.IsNotNull(coordinator.GetState(firstNas.Id));
        Assert.AreEqual("10%", second.CpuText);
        Assert.AreEqual(2, factory.ConnectCount);
    }

    [TestMethod]
    public async Task DeletedNasDoesNotSilentlyReplaceWidgetSourceWithAnotherDevice()
    {
        using var repository = new NasProfileRepository(Path.Combine(directory, "profiles.json"));
        var profiles = new NasProfileService(repository, new EmptySecrets());
        await using var coordinator =
            new NasMonitoringCoordinator(new CountingSessions(), profiles, TimeProvider.System);
        using var presenter = new WidgetMonitoringPresenter(coordinator, false);
        var firstNas = new NasProfile(Guid.NewGuid(), "Primary", new Uri("https://primary.example:5001"), "fixture");
        var secondNas = new NasProfile(Guid.NewGuid(), "Backup", new Uri("https://backup.example:5001"), "fixture");
        var storage = new WidgetSettingsStore(directory);
        await storage.SaveNasSelectionAsync(secondNas.Id, CancellationToken.None);
        var viewModel = new WidgetViewModel();
        await presenter.RegisterAsync(viewModel, storage, CancellationToken.None);
        await presenter.SynchronizeAsync([firstNas, secondNas], CancellationToken.None);
        Assert.AreEqual(secondNas.Id, viewModel.SelectedNas!.Id);

        await presenter.SynchronizeAsync([firstNas], CancellationToken.None);

        Assert.IsNull(viewModel.SelectedNas);
        Assert.AreEqual(secondNas.Id, await storage.LoadNasSelectionAsync(CancellationToken.None));
        await presenter.SynchronizeAsync([firstNas, secondNas], CancellationToken.None);
        Assert.AreEqual(secondNas.Id, viewModel.SelectedNas!.Id);
    }

    [TestMethod]
    public void OnlyOneWindowCanOwnTheDesktopPointerGesture()
    {
        var gestures = new WidgetGestureCoordinator();
        object first = new();
        object second = new();

        Assert.IsTrue(gestures.TryAcquire(first));
        Assert.IsFalse(gestures.TryAcquire(second));
        gestures.Release(second);
        Assert.IsFalse(gestures.TryAcquire(second));
        gestures.Release(first);
        Assert.IsTrue(gestures.TryAcquire(second));
    }

    private sealed class CountingSessions : INasMetricsSessionFactory
    {
        internal int ConnectCount
        {
            get;
            private set;
        }

        public Task<INasMetricsSession> ConnectAsync(NasProfile profile,
            string password,
            string? otp,
            CancellationToken cancellationToken)
        {
            ConnectCount++;
            return Task.FromResult<INasMetricsSession>(new Session(profile.Id, ConnectCount * 10));
        }
    }

    private sealed class Session(Guid nasId, double cpu) : INasMetricsSession
    {
        public Task<NasSnapshot> ReadAsync(CancellationToken cancellationToken, bool forceRefresh = false)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var snapshot = new NasSnapshot(nasId, now, NasMetric.Create(cpu, now), NasMetric.Create(62, now));
            return Task.FromResult(snapshot);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptySecrets : ISecretStore
    {
        public Task WriteAsync(Guid credentialId, string password, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(Guid credentialId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }

        public Task DeleteAsync(Guid credentialId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

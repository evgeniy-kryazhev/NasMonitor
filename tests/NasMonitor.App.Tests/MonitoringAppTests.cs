using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using NasMonitor.App.Monitoring;
using NasMonitor.Core;
using Xunit;

namespace NasMonitor.App.Tests;

public sealed class MonitoringAppTests
{
    [Fact]
    public async Task ProfilesPersistOnlyMetadataAndReconnectReplacesOneWorker()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nasmonitor-tests-" + Guid.NewGuid());
        var path = Path.Combine(directory, "profiles.json");
        var vault = new FakeVault();
        var sessions = new List<BlockingSession>();
        await using var workspace = new MonitoringWorkspace(new ProfileStore(path), vault,
            (_, _, _) => { var session = new BlockingSession(); sessions.Add(session); return session; });
        try
        {
            await workspace.InitializeAsync();
            var first = NasProfile.Create("Home", "https://home.test:5001/", "reader", true);
            var second = NasProfile.Create("Office", "https://office.test:5001/", "reader");
            await workspace.ConnectAsync(first, new("reader", "fixture-password"), "654321");
            await workspace.ConnectAsync(second, new("reader", "session-only"), null);
            Assert.Equal(2, workspace.Profiles.Count);
            Assert.Equal("fixture-password", vault.Read(first.Id)!.Password);
            Assert.Null(vault.Read(second.Id));
            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("fixture-password", json);
            Assert.DoesNotContain("session-only", json);
            Assert.DoesNotContain("654321", json);
            Assert.Equal(2, (await new ProfileStore(path).LoadAsync(default)).Count);
            await workspace.ConnectAsync(first, new("reader", "replacement"), null);
            Assert.True(sessions[0].Disposed);
            Assert.Equal(2, workspace.Profiles.Count);
            Assert.Equal(3, sessions.Count);
            await workspace.RemoveAsync(first.Id);
            Assert.Null(vault.Read(first.Id));
            Assert.Single(workspace.Profiles);
            await workspace.DisconnectAsync(second.Id);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaFact]
    public async Task TwoWidgetsUseOneCachedNasSnapshotAndAreNotDemoWidgets()
    {
        var profile = NasProfile.Create("Home NAS", "https://nas.test:5001/", "reader");
        var session = new ReadySession();
        await using var monitor = new NasMonitorService(delay: (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        monitor.Start(profile, session);
        var one = new WidgetWindow();
        var two = new WidgetWindow();
        try
        {
            one.BindMonitor(profile, monitor);
            two.BindMonitor(profile, monitor);
            one.Show(); two.Show();
            foreach (var widget in new[] { one, two })
            {
                var labels = widget.GetLogicalDescendants().OfType<TextBlock>().Select(x => x.Text).ToArray();
                Assert.Contains("Home NAS", labels);
                Assert.Contains(labels, label => label?.Contains("CPU 20%") == true);
                Assert.DoesNotContain(labels, label => label?.Contains("DEMO") == true);
            }
            Assert.Equal(1, session.Reads);
        }
        finally { one.Close(); two.Close(); }
        Assert.NotNull(monitor.Get(profile.Id)); // Closing views must not disconnect the shared NAS worker.
    }

    [AvaloniaFact]
    public async Task DashboardLoadsMultipleSavedProfilesAndOffersRealConnectionForm()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nasmonitor-ui-" + Guid.NewGuid());
        var store = new ProfileStore(Path.Combine(directory, "profiles.json"));
        await using var workspace = new MonitoringWorkspace(store, new FakeVault());
        var window = new Window();
        try
        {
            await store.SaveAsync([NasProfile.Create("Home", "https://home.test/", "reader"),
                NasProfile.Create("Office", "https://office.test/", "reader")], default);
            await workspace.InitializeAsync();
            var dashboard = new MonitoringDashboard(workspace);
            window.Content = dashboard;
            window.Show();
            var labels = dashboard.GetLogicalDescendants().OfType<TextBlock>().Select(x => x.Text).ToArray();
            Assert.Contains("Home", labels);
            Assert.Contains("Office", labels);
            var boxes = dashboard.GetLogicalDescendants().OfType<TextBox>().ToArray();
            Assert.Contains(boxes, box => box.Watermark == "Password" && box.PasswordChar == '●');
            Assert.Contains(boxes, box => box.Watermark == "https://nas.local:5001/");
            Assert.Equal(2, workspace.Profiles.Count); // Initialization is idempotent on attach.
            dashboard.StopObserving();
        }
        finally { window.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task ConnectionFormSavesProfileDisplaysMetricsAndDisconnectsAndRemovesNas()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nasmonitor-form-" + Guid.NewGuid());
        var vault = new FakeVault();
        await using var workspace = new MonitoringWorkspace(new ProfileStore(Path.Combine(directory, "profiles.json")),
            vault, (_, _, _) => new ReadySession());
        var dashboard = new MonitoringDashboard(workspace);
        var window = new Window { Content = dashboard };
        try
        {
            window.Show();
            await dashboard.CurrentOperation.WaitAsync(TimeSpan.FromSeconds(5));
            var boxes = dashboard.GetLogicalDescendants().OfType<TextBox>().ToArray();
            boxes.Single(x => x.Watermark == "NAS name, e.g. Home DS1821+").Text = "My NAS";
            boxes.Single(x => x.Watermark == "https://nas.local:5001/").Text = "https://nas.test:5001/";
            boxes.Single(x => x.Watermark == "DSM username").Text = "reader";
            var password = boxes.Single(x => x.Watermark == "Password");
            password.Text = "fixture-password";
            void Click(string content) => dashboard.GetLogicalDescendants().OfType<Button>()
                .Single(x => Equals(x.Content, content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click("Connect / save NAS");
            await dashboard.CurrentOperation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("", password.Text);
            Assert.Single(workspace.Profiles);
            Assert.Contains(dashboard.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text?.Contains("CPU 20%") == true);
            NasProfile? shown = null;
            dashboard.WidgetRequested += profile => shown = profile;
            Click("Show widget");
            Assert.Equal(workspace.Profiles[0].Id, shown!.Id);
            Click("Disconnect");
            await dashboard.CurrentOperation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(workspace.Monitor.Get(shown.Id));
            Click("Remove NAS");
            await dashboard.CurrentOperation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(workspace.Profiles);
        }
        finally
        {
            dashboard.StopObserving(); window.Close();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CredentialStringRepresentationCannotExposePassword() =>
        Assert.DoesNotContain("fixture-password", new NasCredentials("reader", "fixture-password").ToString());

    private sealed class FakeVault : ICredentialStore
    {
        private readonly Dictionary<Guid, NasCredentials> _items = [];
        public bool IsAvailable => true;
        public NasCredentials? Read(Guid id) => _items.GetValueOrDefault(id);
        public void Write(Guid id, NasCredentials credentials) => _items[id] = credentials;
        public void Delete(Guid id) => _items.Remove(id);
    }
    private sealed class BlockingSession : INasSession
    {
        public bool Disposed;
        public async Task<NasSnapshot> ReadAsync(CancellationToken token)
        { await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new InvalidOperationException(); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class ReadySession : INasSession
    {
        public int Reads;
        public Task<NasSnapshot> ReadAsync(CancellationToken token)
        { Reads++; return Task.FromResult(new NasSnapshot(DateTimeOffset.UtcNow, 20, 42, [], [], [], [])); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using NasMonitor.App.Platform;
using Xunit;

namespace NasMonitor.App.Tests;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void MainWindowRestoresWidgetWithoutClickingInsideWidget()
    {
        var host = new TestHost();
        var widget = new WidgetWindow(host);
        var main = new MainWindow(() => widget);
        try
        {
            main.Show();
            var restore = Button(main, "Restore widget to normal window");
            Assert.False(restore.IsEnabled);
            Click(Button(main, "Open demo widget"));
            Assert.True(widget.IsVisible);
            Assert.True(restore.IsEnabled);
            Click(restore);
            Assert.Equal(1, host.DetachCalls);
            Assert.Equal(HostState.Detached, widget.HostState);
            Assert.Contains(main.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == "Normal window restored");
        }
        finally { main.Close(); }
        Assert.False(widget.IsVisible);
        Assert.Equal(2, host.DetachCalls);
    }

    [AvaloniaFact]
    public void FailedRestoreIsShownInMainWindowAndCanBeRetried()
    {
        var host = new TestHost { Result = new(HostState.Unavailable, "Restore failed; retry detach.") };
        var widget = new WidgetWindow(host);
        var main = new MainWindow(() => widget);
        try
        {
            main.Show();
            Click(Button(main, "Open demo widget"));
            var restore = Button(main, "Restore widget to normal window");
            Click(restore);
            Assert.Equal(HostState.Unavailable, widget.HostState);
            Assert.Contains(main.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == host.Result.Message);
            Assert.True(restore.IsEnabled);
            host.Result = new(HostState.Detached, "Normal window restored");
            Click(restore);
            Assert.Equal(2, host.DetachCalls);
            Assert.Equal(HostState.Detached, widget.HostState);
        }
        finally { main.Close(); }
    }

    [AvaloniaFact]
    public void OpeningExistingWidgetRestoresItInsteadOfCreatingDuplicate()
    {
        var host = new TestHost();
        var widget = new WidgetWindow(host);
        var created = 0;
        var main = new MainWindow(() => { created++; return widget; });
        try
        {
            main.Show();
            var open = Button(main, "Open demo widget");
            Click(open);
            Click(open);
            Assert.Equal(1, created);
            Assert.Equal(1, host.DetachCalls);
        }
        finally { main.Close(); }
    }

    [AvaloniaFact]
    public void ClosingWidgetDisablesRecoveryAndCanOpenFreshWidget()
    {
        var widgets = new List<WidgetWindow>();
        var main = new MainWindow(() =>
        {
            var widget = new WidgetWindow(new TestHost());
            widgets.Add(widget);
            return widget;
        });
        try
        {
            main.Show();
            Click(Button(main, "Open demo widget"));
            widgets[0].Close();
            Assert.False(Button(main, "Restore widget to normal window").IsEnabled);
            Click(Button(main, "Open demo widget"));
            Assert.Equal(2, widgets.Count);
            Assert.True(widgets[1].IsVisible);
            Assert.True(Button(main, "Restore widget to normal window").IsEnabled);
        }
        finally { main.Close(); }
        Assert.All(widgets, widget => Assert.False(widget.IsVisible));
    }

    private static Button Button(Window window, string content) => window.GetLogicalDescendants()
        .OfType<Button>().Single(x => Equals(x.Content, content));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    private sealed class TestHost : IDesktopHost
    {
        public int DetachCalls;
        public HostResult Result = new(HostState.Detached, "Normal window restored");
        public HostResult Attach(IntPtr window) => new(HostState.Attached, "Experimental attachment");
        public HostResult Check() => new(HostState.Detached, "Normal window restored");
        public HostResult Detach() { DetachCalls++; return Result; }
        public void Dispose() { }
    }
}

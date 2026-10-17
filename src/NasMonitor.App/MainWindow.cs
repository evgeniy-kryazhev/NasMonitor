using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NasMonitor.App.Monitoring;
using NasMonitor.Core;

namespace NasMonitor.App;

public sealed class MainWindow : Window
{
    private WidgetWindow? _widget;
    private readonly Func<WidgetWindow> _createWidget;
    private readonly Button _restore;
    private readonly TextBlock _widgetStatus;
    private bool _closing;
    private int _recoveryVersion;
    private bool _shutdownComplete;
    private readonly MonitoringWorkspace? _workspace;
    private NasProfile? _widgetProfile;

    public MainWindow() : this(() => new WidgetWindow(), new MonitoringWorkspace()) { }

    internal MainWindow(Func<WidgetWindow> createWidget) : this(createWidget, null) { }

    private MainWindow(Func<WidgetWindow> createWidget, MonitoringWorkspace? workspace)
    {
        _workspace = workspace;
        _createWidget = createWidget;
        Title = "NAS Monitor";
        Width = 560;
        Height = 480;
        MinWidth = 420;
        MinHeight = 280;

        var root = new StackPanel { Margin = new Avalonia.Thickness(28), Spacing = 16 };
        root.Children.Add(new TextBlock
        {
            Text = "NAS Monitor",
            FontSize = 30,
            FontWeight = FontWeight.Bold
        });
        root.Children.Add(new TextBlock
        {
            Text = "Stage 1: cross-platform Avalonia widget shell. NAS metrics below are demo values.",
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock
        {
            Text = "Target: widgets behind application windows, embedded in the desktop.",
            TextWrapping = TextWrapping.Wrap
        });
        var open = new Button
        {
            Content = "Open demo widget",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Avalonia.Thickness(20, 10)
        };
        open.Click += (_, _) =>
        {
            if (_closing) return;
            if (_widgetProfile is not null)
            {
                _recoveryVersion++;
                _widget?.Close();
                _widgetProfile = null;
            }
            if (_widget is { IsVisible: true })
            {
                RestoreWidget();
                return;
            }
            OpenWidget(false);
        };
        root.Children.Add(open);
        _restore = new Button { Content = "Restore widget to normal window", IsEnabled = false };
        _restore.Click += (_, _) => RestoreWidget();
        root.Children.Add(_restore);
        _widgetStatus = new TextBlock { Text = "Widget not opened", TextWrapping = TextWrapping.Wrap };
        root.Children.Add(_widgetStatus);
        root.Children.Add(new TextBlock
        {
            Text = "Windows and macOS desktop hosts are experimental and require real OS verification.",
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        });
        var desktopControls = new ScrollViewer { Content = root };
        MonitoringDashboard? dashboard = null;
        if (workspace is not null)
        {
            dashboard = new MonitoringDashboard(workspace);
            dashboard.WidgetRequested += profile =>
            {
                if (_closing) return;
                _recoveryVersion++;
                var position = _widget?.Position;
                _widget?.Close();
                _widgetProfile = profile;
                OpenWidget(false, position: position);
            };
            Width = 760;
            Height = 820;
            Content = new TabControl { ItemsSource = new[]
            {
                new TabItem { Header = "NAS monitoring", Content = dashboard },
                new TabItem { Header = "Desktop widget (experimental)", Content = desktopControls }
            } };
        }
        else Content = desktopControls;
        Closing += async (_, args) =>
        {
            if (_closing) { args.Cancel = !_shutdownComplete; return; }
            _closing = true;
            _recoveryVersion++;
            _widget?.Close();
            if (_workspace is null) { _shutdownComplete = true; return; }
            args.Cancel = true;
            dashboard?.StopObserving();
            IsEnabled = false;
            try { await _workspace.DisposeAsync(); }
            finally { _shutdownComplete = true; Avalonia.Threading.Dispatcher.UIThread.Post(Close); }
        };
    }

    private void RestoreWidget()
    {
        if (_widget is not { IsVisible: true } widget) return;
        _recoveryVersion++;
        var result = widget.RestoreNormalWindow();
        if (result.State == Platform.HostState.Detached) widget.Activate();
    }

    private void OpenWidget(bool attach, int recoveryAttempts = 0, Avalonia.PixelPoint? position = null)
    {
        if (_closing) return;
        var widget = _createWidget();
        if (_widgetProfile is { } profile && _workspace is not null) widget.BindMonitor(profile, _workspace.Monitor);
        _widget = widget;
        _restore.IsEnabled = true;
        _widgetStatus.Text = widget.StatusText;
        widget.StatusChanged += (_, _) =>
        {
            if (ReferenceEquals(_widget, widget)) _widgetStatus.Text = widget.StatusText;
        };
        widget.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_widget, widget)) return;
            _restore.IsEnabled = false;
            _widgetStatus.Text = "Widget closed";
        };
        if (position is { } initialPosition) widget.Position = initialPosition;
        widget.RecreationRequested += (_, _) =>
        {
            if (_closing) return;
            var version = _recoveryVersion;
            // Explorer can destroy child HWNDs. Never reuse a dead native handle.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_closing || !IsVisible || version != _recoveryVersion || !ReferenceEquals(_widget, widget)) return;
                var savedPosition = widget.Position;
                widget.Close();
                OpenWidget(recoveryAttempts < 3, recoveryAttempts + 1, savedPosition);
            });
        };
        if (attach) widget.Opened += (_, _) => widget.RequestAttachment();
        widget.Show();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NasMonitor.App.Platform;
using NasMonitor.App.Monitoring;
using NasMonitor.Core;

namespace NasMonitor.App;

public sealed class WidgetWindow : Window
{
    private readonly TextBlock _status;
    private readonly TextBlock _nasName, _metrics, _connection;
    private NasMonitorService? _monitor;
    private Action<MonitorUpdate>? _onMetrics;
    private Guid? _boundProfileId;

    public void BindMonitor(NasProfile profile, NasMonitorService monitor)
    {
        UnbindMonitor();
        _nasName.Text = profile.Name;
        Title = $"NAS Widget — {profile.Name}";
        void Render(MonitorUpdate? update)
        {
            _metrics.Text = MetricsText.Format(update);
            _connection.Text = MetricsText.State(update);
        }
        _monitor = monitor;
        _boundProfileId = profile.Id;
        _onMetrics = update =>
        {
            if (update.Profile.Id == profile.Id) Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_monitor, monitor) && _boundProfileId == profile.Id) Render(update);
            });
        };
        monitor.Updated += _onMetrics;
        Render(monitor.Get(profile.Id));
    }
    private void UnbindMonitor()
    {
        if (_monitor is not null && _onMetrics is not null) _monitor.Updated -= _onMetrics;
        _monitor = null;
        _boundProfileId = null;
        _onMetrics = null;
    }
    private readonly IDesktopHost _host;
    private readonly DispatcherTimer _recovery = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _requested;
    public event EventHandler? RecreationRequested;
    public event EventHandler? StatusChanged;
    public string StatusText => _status.Text ?? string.Empty;
    public HostState HostState { get; private set; } = HostState.Detached;

    public HostResult RestoreNormalWindow()
    {
        // An explicit restore cancels automatic attachment even if rollback needs another attempt.
        _requested = false;
        _recovery.Stop();
        var result = _host.Detach();
        if (result.State == HostState.Detached) EnsureReachable();
        Display(result);
        return result;
    }

    private void EnsureReachable()
    {
        var size = new PixelSize((int)Math.Ceiling(Bounds.Width * RenderScaling),
            (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var position = WindowPlacement.KeepReachable(Position, size,
            Screens.All.Select(screen => screen.WorkingArea).ToArray(), RenderScaling);
        if (position != Position) Position = position;
    }

    private void OnScreensChanged(object? sender, EventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Do not reposition a native child window through Avalonia's top-level coordinates.
            if (IsVisible && HostState == HostState.Detached) EnsureReachable();
        });
    }

    public void RequestAttachment()
    {
        var handle = TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero
            || (OperatingSystem.IsWindows() && handle.HandleDescriptor != "HWND")
            || (OperatingSystem.IsMacOS() && handle.HandleDescriptor != "NSWindow"))
        {
            Display(new(HostState.Unavailable, "Expected native window handle unavailable; no changes made."));
            return;
        }
        _requested = true;
        Display(_host.Attach(handle.Handle));
        if (_requested) _recovery.Start();
    }

    private void Display(HostResult result)
    {
        HostState = result.State;
        _status.Text = result.Message;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        _recovery.Interval = result.State == HostState.Unavailable
            ? TimeSpan.FromSeconds(Math.Min(30, _recovery.Interval.TotalSeconds * 2))
            : TimeSpan.FromSeconds(2);
        if (result.State == HostState.WindowLost)
        {
            _requested = false;
            _recovery.Stop();
            RecreationRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public WidgetWindow() : this(OperatingSystem.IsWindows() ? new WindowsExplorerDesktopHost()
        : OperatingSystem.IsMacOS() ? new MacOSDesktopHost() : new UnsupportedDesktopHost()) { }

    internal WidgetWindow(IDesktopHost host)
    {
        _host = host;
        Title = "NAS Widget — Demo";
        Width = 315;
        Height = 380;
        MinWidth = 270;
        MinHeight = 230;
        CanResize = true;
        ShowInTaskbar = false;
        Topmost = false;
        Background = new SolidColorBrush(Color.Parse("#182331"));

        var root = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        _nasName = new TextBlock { Text = "● DS1821+  (DEMO)", FontSize = 20, FontWeight = FontWeight.SemiBold };
        _metrics = new TextBlock { Text = "CPU 12%    RAM 38%\nStorage 64%    Network ↓ 2.4 MB/s", FontSize = 15, TextWrapping = TextWrapping.Wrap };
        _connection = new TextBlock { Text = "Not connected to a real NAS.", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
        root.Children.Add(_nasName);
        root.Children.Add(_metrics);
        root.Children.Add(_connection);
        var attach = new Button { Content = "Try desktop attachment", HorizontalAlignment = HorizontalAlignment.Left };
        _status = new TextBlock { Text = "Normal window mode", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
        attach.Click += (_, _) => RequestAttachment();
        var detach = new Button { Content = "Restore normal window" };
        detach.Click += (_, _) => RestoreNormalWindow();
        _recovery.Tick += (_, _) =>
        {
            if (!_requested) return;
            var result = _host.Check();
            if (result.State == HostState.Detached) RequestAttachment();
            else Display(result);
        };
        root.Children.Add(attach);
        root.Children.Add(detach);
        root.Children.Add(_status);
        Content = new ScrollViewer { Content = root };
        Opened += (_, _) =>
        {
            EnsureReachable();
            Screens.Changed += OnScreensChanged;
        };
        // Restore native state BEFORE Avalonia destroys NSWindow/HWND. Closed is too late on macOS.
        Closing += (_, _) =>
        {
            _recovery.Stop();
            if (_requested && OperatingSystem.IsWindows()) Display(_host.Check());
            _requested = false;
            Display(_host.Detach());
        };
        Closed += (_, _) =>
        {
            _recovery.Stop();
            Screens.Changed -= OnScreensChanged;
            UnbindMonitor();
            // A native HWND destruction may close Avalonia without a managed Closing event.
            if (_requested && OperatingSystem.IsWindows()) Display(_host.Check());
            _requested = false;
        };
    }
}

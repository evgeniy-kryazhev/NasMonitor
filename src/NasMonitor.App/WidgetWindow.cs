using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NasMonitor.App.Platform;

namespace NasMonitor.App;

public sealed class WidgetWindow : Window
{
    private readonly TextBlock _status;
    private readonly IDesktopHost _host = OperatingSystem.IsWindows() ? new WindowsExplorerDesktopHost()
        : OperatingSystem.IsMacOS() ? new MacOSDesktopHost() : new UnsupportedDesktopHost();
    private readonly DispatcherTimer _recovery = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _requested;
    public event EventHandler? RecreationRequested;

    public void RequestAttachment()
    {
        var handle = TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero
            || (OperatingSystem.IsWindows() && handle.HandleDescriptor != "HWND")
            || (OperatingSystem.IsMacOS() && handle.HandleDescriptor != "NSWindow"))
        {
            _status.Text = "Expected native window handle unavailable; no changes made.";
            return;
        }
        _requested = true;
        Display(_host.Attach(handle.Handle));
        if (_requested) _recovery.Start();
    }

    private void Display(HostResult result)
    {
        _status.Text = result.Message;
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

    public WidgetWindow()
    {
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
        root.Children.Add(new TextBlock { Text = "● DS1821+  (DEMO)", FontSize = 20, FontWeight = FontWeight.SemiBold });
        root.Children.Add(new TextBlock { Text = "CPU 12%    RAM 38%\nStorage 64%    Network ↓ 2.4 MB/s", FontSize = 15, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = "Not connected to a real NAS.", Foreground = Brushes.Gray });
        var attach = new Button { Content = "Try desktop attachment", HorizontalAlignment = HorizontalAlignment.Left };
        _status = new TextBlock { Text = "Normal window mode", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
        attach.Click += (_, _) => RequestAttachment();
        var detach = new Button { Content = "Restore normal window" };
        detach.Click += (_, _) =>
        {
            var result = _host.Detach();
            Display(result);
            if (result.State == HostState.Detached)
            {
                _requested = false;
                _recovery.Stop();
            }
        };
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
        Content = root;
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
            // A native HWND destruction may close Avalonia without a managed Closing event.
            if (_requested && OperatingSystem.IsWindows()) Display(_host.Check());
            _requested = false;
        };
    }
}

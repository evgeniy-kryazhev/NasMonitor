using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NasMonitor.App.Platform;

namespace NasMonitor.App;

public sealed class WidgetWindow : Window
{
    private readonly TextBlock _status;
    private readonly WindowsExplorerDesktopHost _windowsHost = new();
    private bool _attached;

    public WidgetWindow()
    {
        Title = "NAS Widget — Demo";
        Width = 315;
        Height = 290;
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
        attach.Click += (_, _) =>
        {
            if (_attached)
            {
                _status.Text = "Already attached. Close widget to detach.";
                return;
            }
            if (!OperatingSystem.IsWindows())
            {
                _status.Text = "Desktop attachment not implemented for this OS.";
                return;
            }
            if (_windowsHost.TryAttach(this, out var reason))
            {
                _attached = true;
                _status.Text = "Experimental desktop attachment requested. Verify behavior manually.";
            }
            else _status.Text = reason;
        };
        root.Children.Add(attach);
        root.Children.Add(_status);
        Content = root;
        Closed += (_, _) =>
        {
            if (_attached) _windowsHost.Detach();
        };
    }
}

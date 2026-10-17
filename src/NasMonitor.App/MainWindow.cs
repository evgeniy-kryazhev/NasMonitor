using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace NasMonitor.App;

public sealed class MainWindow : Window
{
    private WidgetWindow? _widget;

    public MainWindow()
    {
        Title = "NAS Monitor — Desktop Widget Spike";
        Width = 560;
        Height = 340;
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
            if (_widget is { IsVisible: true })
            {
                _widget.Activate();
                return;
            }
            OpenWidget(false);
        };
        root.Children.Add(open);
        root.Children.Add(new TextBlock
        {
            Text = "Windows and macOS desktop hosts are experimental and require real OS verification.",
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        });
        Content = root;
    }

    private void OpenWidget(bool attach, int recoveryAttempts = 0, Avalonia.PixelPoint? position = null)
    {
        var widget = new WidgetWindow();
        _widget = widget;
        if (position is { } initialPosition) widget.Position = initialPosition;
        widget.RecreationRequested += (_, _) =>
        {
            // Explorer can destroy child HWNDs. Never reuse a dead native handle.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!IsVisible || !ReferenceEquals(_widget, widget)) return;
                var savedPosition = widget.Position;
                widget.Close();
                OpenWidget(recoveryAttempts < 3, recoveryAttempts + 1, savedPosition);
            });
        };
        if (attach) widget.Opened += (_, _) => widget.RequestAttachment();
        widget.Show();
    }
}

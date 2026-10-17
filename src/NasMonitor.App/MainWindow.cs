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
            _widget = new WidgetWindow();
            _widget.Show();
        };
        root.Children.Add(open);
        root.Children.Add(new TextBlock
        {
            Text = "Windows desktop attachment is experimental. macOS desktop attachment is pending.",
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        });
        Content = root;
    }
}

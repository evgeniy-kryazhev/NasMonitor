namespace NasMonitor.Core.Widgets;

/// <summary>Saved screen position in physical pixels; size in logical pixels.</summary>
public sealed record WidgetLayout(int X = 80, int Y = 100, double Width = 300, double Height = 210, bool Locked = true)
{
    /// <summary>Preserve a user-requested height while preventing the content from being clipped.</summary>
    public WidgetLayout WithContentMinimum(double contentHeight)
    {
        double minimumHeight = double.IsFinite(contentHeight) ? Math.Clamp(contentHeight, 72, 600) : 72;
        double requestedHeight = double.IsFinite(Height) ? Height : minimumHeight;
        return this with { Height = Math.Clamp(requestedHeight, minimumHeight, 600) };
    }

    /// <summary>Clamp invalid or disconnected-screen placement into a visible work area.</summary>
    public WidgetLayout Fit(IReadOnlyList<DisplayArea> displays)
    {
        double width = double.IsFinite(Width) ? Math.Clamp(Width, 260, 720) : 300;
        double height = double.IsFinite(Height) ? Math.Clamp(Height, 72, 600) : 210;
        DisplayArea display = displays.FirstOrDefault(area => area.Contains(X, Y))
                              ?? displays.FirstOrDefault() ?? new DisplayArea(0, 0, 1920, 1080, 1);
        width = Math.Min(width, display.Width / display.Scale);
        height = Math.Min(height, display.Height / display.Scale);
        int maximumX = display.X + display.Width - (int)Math.Ceiling(width * display.Scale);
        int maximumY = display.Y + display.Height - (int)Math.Ceiling(height * display.Scale);
        return this with
        {
            X = Math.Clamp(X, display.X, Math.Max(display.X, maximumX)),
            Y = Math.Clamp(Y, display.Y, Math.Max(display.Y, maximumY)),
            Width = width,
            Height = height
        };
    }
}

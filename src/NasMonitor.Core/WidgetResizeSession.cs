namespace NasMonitor.Core;

/// <summary>Resizes from a fixed physical pointer anchor while retaining the top-left corner.</summary>
public sealed class WidgetResizeSession
{
    private readonly WidgetLayout initialLayout;
    private readonly int pointerX;
    private readonly int pointerY;
    private readonly double initialScale;

    /// <summary>Create a bottom-right resize gesture.</summary>
    /// <param name="initialLayout">Validated layout at the beginning of the gesture.</param>
    /// <param name="pointerX">Initial physical screen X.</param>
    /// <param name="pointerY">Initial physical screen Y.</param>
    /// <param name="initialScale">Monitor scale at the beginning of the gesture.</param>
    public WidgetResizeSession(WidgetLayout initialLayout, int pointerX, int pointerY, double initialScale)
    {
        ArgumentNullException.ThrowIfNull(initialLayout);
        if (!double.IsFinite(initialScale) || initialScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialScale));
        }
        this.initialLayout = initialLayout;
        this.pointerX = pointerX;
        this.pointerY = pointerY;
        this.initialScale = initialScale;
    }

    /// <summary>Calculate logical size from physical pointer displacement.</summary>
    /// <param name="currentX">Current physical screen X.</param>
    /// <param name="currentY">Current physical screen Y.</param>
    /// <param name="displays">Available work areas, primary first.</param>
    /// <returns>A layout inside the available work area.</returns>
    public WidgetLayout ResizeTo(int currentX, int currentY, IReadOnlyList<DisplayArea> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        WidgetLayout fitted = initialLayout.Fit(displays);
        DisplayArea display = displays.FirstOrDefault(area => area.Contains(fitted.X, fitted.Y))
            ?? new DisplayArea(0, 0, 1920, 1080, 1);
        double maximumWidth = Math.Min(720, ((double)display.X + display.Width - fitted.X) / display.Scale);
        double maximumHeight = Math.Min(600, ((double)display.Y + display.Height - fitted.Y) / display.Scale);
        double requestedWidth = initialLayout.Width + ((long)currentX - pointerX) / initialScale;
        double requestedHeight = initialLayout.Height + ((long)currentY - pointerY) / initialScale;

        // Clamp size rather than moving the origin when the pointer crosses a screen edge.
        return fitted with
        {
            Width = Math.Clamp(requestedWidth, Math.Min(260, maximumWidth), maximumWidth),
            Height = Math.Clamp(requestedHeight, Math.Min(72, maximumHeight), maximumHeight)
        };
    }

    /// <summary>Whether a physical point falls inside the 24-DIP bottom-right resize grip.</summary>
    /// <param name="layout">Current layout.</param>
    /// <param name="x">Physical screen X.</param>
    /// <param name="y">Physical screen Y.</param>
    /// <param name="scale">Current monitor scale.</param>
    /// <returns>True only inside the visible grip and while unlocked.</returns>
    public static bool IsOverGrip(WidgetLayout layout, int x, int y, double scale)
    {
        double right = layout.X + layout.Width * scale;
        double bottom = layout.Y + layout.Height * scale;
        return !layout.Locked && x >= right - 24 * scale && x < right &&
            y >= bottom - 24 * scale && y < bottom;
    }
}

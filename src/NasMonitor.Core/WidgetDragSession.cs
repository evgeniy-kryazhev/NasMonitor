namespace NasMonitor.Core;

/// <summary>Moves from a fixed physical screen anchor, without accumulating rounding or clamping errors.</summary>
public sealed class WidgetDragSession(WidgetLayout initialLayout, int pointerX, int pointerY)
{
    /// <summary>Calculate placement from the current pointer and available monitor work areas.</summary>
    /// <param name="currentX">Current physical screen X.</param>
    /// <param name="currentY">Current physical screen Y.</param>
    /// <param name="displays">Available monitor work areas, primary first.</param>
    /// <returns>A visible layout preserving the original size and lock state.</returns>
    public WidgetLayout MoveTo(int currentX, int currentY, IReadOnlyList<DisplayArea> displays)
    {
        var requested = initialLayout with
        {
            X = (int)Math.Clamp((long)initialLayout.X + currentX - pointerX, int.MinValue, int.MaxValue),
            Y = (int)Math.Clamp((long)initialLayout.Y + currentY - pointerY, int.MinValue, int.MaxValue)
        };
        return requested.Fit(displays);
    }
}

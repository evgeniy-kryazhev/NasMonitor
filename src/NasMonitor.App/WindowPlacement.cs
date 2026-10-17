using Avalonia;

namespace NasMonitor.App;

internal static class WindowPlacement
{
    /// <summary>Keep a title/drag strip reachable in physical work-area coordinates.</summary>
    public static PixelPoint KeepReachable(PixelPoint position, PixelSize size,
        IReadOnlyList<PixelRect> workAreas, double scale = 1)
    {
        if (size.Width <= 0 || size.Height <= 0) return position;
        var grabWidth = Math.Min(size.Width, Math.Ceiling(96 * scale));
        var grabHeight = Math.Min(size.Height, Math.Ceiling(32 * scale));
        PixelPoint? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var area in workAreas)
        {
            if (area.Width <= 0 || area.Height <= 0) continue;
            var visibleWidth = Math.Min((double)position.X + size.Width, area.Right) - Math.Max(position.X, area.X);
            var visibleHeight = Math.Min((double)position.Y + grabHeight, area.Bottom) - Math.Max(position.Y, area.Y);
            if (visibleWidth >= grabWidth && visibleHeight >= grabHeight) return position;
            var candidate = new PixelPoint(
                Math.Clamp(position.X, area.X, Math.Max(area.X, area.Right - size.Width)),
                Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Bottom - size.Height)));
            var dx = (double)candidate.X - position.X;
            var dy = (double)candidate.Y - position.Y;
            var distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = candidate;
        }
        return best ?? position;
    }
}

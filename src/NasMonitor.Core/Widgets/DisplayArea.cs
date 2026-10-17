namespace NasMonitor.Core.Widgets;

/// <summary>Monitor work area in physical pixels and its scale.</summary>
public sealed record DisplayArea(int X, int Y, int Width, int Height, double Scale)
{
    /// <summary>Test a physical point against the monitor rectangle.</summary>
    public bool Contains(int x, int y)
    {
        return x >= X && y >= Y && x < (long)X + Width && y < (long)Y + Height;
    }
}

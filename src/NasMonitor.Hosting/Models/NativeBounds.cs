namespace NasMonitor.Hosting.Models;

/// <summary>Physical screen coordinates with physical dimensions.</summary>
public readonly record struct NativeBounds(int X, int Y, int Width, int Height);

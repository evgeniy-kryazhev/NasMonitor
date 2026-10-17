namespace NasMonitor.Hosting.Models;

/// <summary>Physical pointer state from a shell that can intercept widget input.</summary>
public readonly record struct DesktopPointer(int X, int Y, bool LeftButtonPressed);

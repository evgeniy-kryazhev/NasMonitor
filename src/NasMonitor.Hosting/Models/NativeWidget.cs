namespace NasMonitor.Hosting.Models;

/// <summary>Native window identity; no UI toolkit dependency.</summary>
public sealed record NativeWidget(nint Handle, string Descriptor);

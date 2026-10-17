using NasMonitor.Hosting.Models;

namespace NasMonitor.Hosting.Abstractions;

/// <summary>Optional native pointer access, used only while editing a desktop widget.</summary>
public interface IDesktopPointerSource
{
    /// <summary>Read the current pointer; false means the interactive desktop is unavailable.</summary>
    public bool TryReadPointer(out DesktopPointer pointer);

    /// <summary>Check whether a press reaches this widget or unobstructed desktop background.</summary>
    public bool CanStartDrag(NativeWidget widget, DesktopPointer pointer);
}

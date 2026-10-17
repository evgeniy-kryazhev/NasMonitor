namespace NasMonitor.Hosting;

/// <summary>Native window identity; no UI toolkit dependency.</summary>
public sealed record NativeWidget(nint Handle, string Descriptor);

/// <summary>Physical screen coordinates with physical dimensions.</summary>
public readonly record struct NativeBounds(int X, int Y, int Width, int Height);

/// <summary>API outcome, deliberately distinct from visual OS acceptance.</summary>
public sealed record DesktopAttachResult(bool Success, string Code);

/// <summary>Physical pointer state from a shell that can intercept widget input.</summary>
public readonly record struct DesktopPointer(int X, int Y, bool LeftButtonPressed);

/// <summary>Optional native pointer access, used only while editing a desktop widget.</summary>
public interface IDesktopPointerSource
{
    /// <summary>Read the current pointer; false means the interactive desktop is unavailable.</summary>
    bool TryReadPointer(out DesktopPointer pointer);

    /// <summary>Check whether a press reaches this widget or unobstructed desktop background.</summary>
    bool CanStartDrag(NativeWidget widget, DesktopPointer pointer);
}

/// <summary>Desktop placement adapter. All calls must run on the UI thread.</summary>
public interface IDesktopWidgetHost
{
    /// <summary>Attach or restore placement; cancellation precedes native calls.</summary>
    DesktopAttachResult Attach(NativeWidget widget, NativeBounds bounds, bool locked, CancellationToken cancellationToken);

    /// <summary>Whether the native window and its desktop parent still exist.</summary>
    bool IsAlive(NativeWidget widget);

    /// <summary>Undo native ownership before destroying the UI window.</summary>
    void Detach(NativeWidget widget);
}

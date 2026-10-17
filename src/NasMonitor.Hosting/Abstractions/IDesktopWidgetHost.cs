using NasMonitor.Hosting.Models;

namespace NasMonitor.Hosting.Abstractions;

/// <summary>Desktop placement adapter. All calls must run on the UI thread.</summary>
public interface IDesktopWidgetHost
{
    /// <summary>Attach or restore placement; cancellation precedes native calls.</summary>
    public DesktopAttachResult Attach(NativeWidget widget,
        NativeBounds bounds,
        bool locked,
        CancellationToken cancellationToken);

    /// <summary>Whether the native window and its desktop parent still exist.</summary>
    public bool IsAlive(NativeWidget widget);

    /// <summary>Undo native ownership before destroying the UI window.</summary>
    public void Detach(NativeWidget widget);
}

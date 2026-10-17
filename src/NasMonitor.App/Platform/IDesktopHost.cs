namespace NasMonitor.App.Platform;

public enum HostState { Detached, Attached, Unavailable, WindowLost }
public readonly record struct HostResult(HostState State, string Message);

/// <summary>All operations run on the Avalonia UI thread; native attachment is experimental.</summary>
public interface IDesktopHost : IDisposable
{
    HostResult Attach(IntPtr window);
    HostResult Check();
    HostResult Detach();
}

public sealed class UnsupportedDesktopHost : IDesktopHost
{
    public HostResult Attach(IntPtr window) => new(HostState.Unavailable, "Desktop hosting is supported only on Windows and macOS.");
    public HostResult Check() => new(HostState.Detached, "Normal window mode");
    public HostResult Detach() => Check();
    public void Dispose() { }
}

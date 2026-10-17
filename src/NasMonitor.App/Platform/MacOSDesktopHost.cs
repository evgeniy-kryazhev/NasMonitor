using System.Runtime.InteropServices;

namespace NasMonitor.App.Platform;

public readonly record struct MacWindowState(nint Level, nuint CollectionBehavior);
public interface IMacDesktopApi
{
    MacWindowState Capture(IntPtr window);
    void Apply(IntPtr window);
    void Restore(IntPtr window, MacWindowState state);
}

/// <summary>
/// AppKit desktop-level experiment, not Finder embedding. All calls require a live NSWindow on the UI thread.
/// Window lifetime remains owned by Avalonia. Spaces behavior needs interactive macOS verification.
/// </summary>
public sealed class MacOSDesktopHost(IMacDesktopApi api) : IDesktopHost
{
    private IntPtr _window;
    private MacWindowState? _original;
    public MacOSDesktopHost() : this(new AppKitDesktopApi()) { }
    public HostResult Attach(IntPtr window)
    {
        if (_original is not null)
            return window == _window ? Check() : new(HostState.Unavailable, "Detach the current window first.");
        if (window == IntPtr.Zero) return new(HostState.Unavailable, "NSWindow handle unavailable.");
        _original = api.Capture(window);
        _window = window;
        api.Apply(window);
        return Attached();
    }
    public HostResult Check()
    {
        if (_original is null) return new(HostState.Detached, "Normal window mode");
        // Avalonia may rewrite level/collection behavior after activation or window property changes.
        // Reassert only when needed, without activating the application or ordering windows repeatedly.
        api.Apply(_window);
        return Attached();
    }
    public HostResult Detach()
    {
        if (_original is null) return new(HostState.Detached, "Normal window mode");
        api.Restore(_window, _original.Value);
        _window = IntPtr.Zero;
        _original = null;
        return new(HostState.Detached, "Original AppKit level and Spaces behavior restored");
    }
    private static HostResult Attached() => new(HostState.Attached, "Experimental AppKit desktop level; Finder/Spaces/Mission Control verification required.");
    public void Dispose() => Detach();
}

internal sealed class AppKitDesktopApi : IMacDesktopApi
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const nuint CanJoinAllSpaces = 1, Stationary = 1 << 4, IgnoresCycle = 1 << 6;
    public MacWindowState Capture(IntPtr window)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        return new(Send(window, Selector("level")), unchecked((nuint)Send(window, Selector("collectionBehavior"))));
    }
    public void Apply(IntPtr window)
    {
        var current = Capture(window);
        // kCGDesktopWindowLevelKey = 2. Just above wallpaper, below desktop icons and normal windows.
        var level = (nint)CGWindowLevelForKey(2) + 1;
        var behavior = CanJoinAllSpaces | Stationary | IgnoresCycle;
        if (current.Level != level) SendValue(window, Selector("setLevel:"), level);
        if (current.CollectionBehavior != behavior) SendValue(window, Selector("setCollectionBehavior:"), unchecked((nint)behavior));
        // Do not activate Finder, reparent to Finder, add FullScreenAuxiliary, or use orderFrontRegardless.
    }
    public void Restore(IntPtr window, MacWindowState state)
    {
        SendValue(window, Selector("setLevel:"), state.Level);
        SendValue(window, Selector("setCollectionBehavior:"), unchecked((nint)state.CollectionBehavior));
    }
    [DllImport(CoreGraphics)] private static extern int CGWindowLevelForKey(int key);
    [DllImport(ObjC, EntryPoint = "sel_registerName")] private static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendValue(IntPtr receiver, IntPtr selector, nint value);
}

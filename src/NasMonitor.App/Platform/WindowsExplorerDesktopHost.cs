using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NasMonitor.App.Platform;

public readonly record struct WindowRect(int Left, int Top, int Right, int Bottom);
public readonly record struct WindowsWindowState(IntPtr Parent, long Style, long ExtendedStyle, WindowRect Bounds, IntPtr Owner = default);

// Boundary allows lifecycle/rollback tests without an Explorer desktop.
public interface IWindowsDesktopApi
{
    bool IsWindow(IntPtr window);
    IntPtr FindDesktop();
    IntPtr ParentOf(IntPtr window);
    WindowsWindowState Capture(IntPtr window);
    void Attach(IntPtr window, IntPtr desktop, WindowsWindowState original);
    void Restore(IntPtr window, WindowsWindowState original);
}

/// <summary>Undocumented WorkerW integration. Native success is not desktop acceptance.</summary>
public sealed class WindowsExplorerDesktopHost(IWindowsDesktopApi api) : IDesktopHost
{
    private IntPtr _window;
    private IntPtr _desktop;
    private WindowsWindowState? _original;

    public WindowsExplorerDesktopHost() : this(new Win32DesktopApi()) { }

    public HostResult Attach(IntPtr window)
    {
        if (_original is not null)
            return window == _window ? Check() : new(HostState.Unavailable, "Detach the current window first.");
        if (!api.IsWindow(window)) return new(HostState.WindowLost, "Widget handle no longer exists.");
        try
        {
            var desktop = api.FindDesktop();
            if (desktop == IntPtr.Zero) return new(HostState.Unavailable, "Explorer WorkerW unavailable; normal window retained.");
            // Capture once, before mutations, including failures requiring rollback.
            _original = api.Capture(window);
            _window = window;
            _desktop = desktop;
            api.Attach(window, desktop, _original.Value);
            return Attached();
        }
        catch (Win32Exception e) { return Rollback(e); }
    }

    public HostResult Check()
    {
        if (_original is null) return new(HostState.Detached, "Normal window mode");
        if (!api.IsWindow(_window))
        {
            Clear();
            return new(HostState.WindowLost, "Explorer destroyed the widget handle; recreate the Avalonia window.");
        }
        try
        {
            // Explorer restart or parent replacement: rediscover; never overwrite original state.
            if (api.IsWindow(_desktop) && api.ParentOf(_window) == _desktop) return Attached();
            var desktop = api.FindDesktop();
            if (desktop == IntPtr.Zero)
            {
                api.Restore(_window, _original.Value);
                _desktop = IntPtr.Zero;
                return new(HostState.Unavailable, "Explorer unavailable; restored normal window. Retrying.");
            }
            api.Attach(_window, desktop, _original.Value);
            _desktop = desktop;
            return Attached();
        }
        catch (Win32Exception e) { return Rollback(e); }
    }

    public HostResult Detach()
    {
        if (_original is null) return new(HostState.Detached, "Normal window mode");
        try
        {
            if (api.IsWindow(_window)) api.Restore(_window, _original.Value);
            Clear();
            return new(HostState.Detached, "Normal window restored");
        }
        catch (Win32Exception e)
        {
            // Retain the snapshot so the user can retry rollback.
            return new(HostState.Unavailable, $"Restore failed (Win32 {e.NativeErrorCode}); retry detach.");
        }
    }

    private HostResult Rollback(Win32Exception error)
    {
        var restored = Detach();
        return new(HostState.Unavailable, $"Desktop attachment failed (Win32 {error.NativeErrorCode}). {restored.Message}");
    }
    private static HostResult Attached() => new(HostState.Attached, "Experimental WorkerW attachment; real Windows verification required.");
    private void Clear() { _window = _desktop = IntPtr.Zero; _original = null; }
    public void Dispose() => Detach();
}

internal sealed class Win32DesktopApi : IWindowsDesktopApi
{
    private const long WsChild = 0x40000000, WsPopup = 0x80000000;
    private const long WsExAppWindow = 0x40000, WsExToolWindow = 0x80;
    public bool IsWindow(IntPtr window) => OperatingSystem.IsWindows() && window != IntPtr.Zero && NativeIsWindow(window);
    public IntPtr ParentOf(IntPtr window) => GetParent(window);
    public IntPtr FindDesktop()
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;
        if (SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), IntPtr.Zero, 0x0002, 1000, out _) == IntPtr.Zero)
            return IntPtr.Zero;
        IntPtr target = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            var candidate = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            if (candidate == IntPtr.Zero) return true;
            target = candidate;
            return false;
        }, IntPtr.Zero);
        return target;
    }
    public WindowsWindowState Capture(IntPtr window)
    {
        if (!GetWindowRect(window, out var r)) throw new Win32Exception();
        var style = GetStyle(window, -16);
        var isChild = (style & WsChild) != 0;
        // GetParent returns the OWNER for top-level windows; ownership is not child parenting.
        return new(isChild ? GetParent(window) : IntPtr.Zero, style, GetStyle(window, -20),
            new(r.Left, r.Top, r.Right, r.Bottom), isChild ? IntPtr.Zero : GetWindow(window, 4)); // GW_OWNER

    }
    public void Attach(IntPtr window, IntPtr desktop, WindowsWindowState original)
    {
        // Preserve the CURRENT screen position across parent/client coordinate systems.
        if (!GetWindowRect(window, out var r)) throw new Win32Exception();
        SetStyle(window, -16, (original.Style | WsChild) & ~WsPopup);
        SetStyle(window, -20, (original.ExtendedStyle | WsExToolWindow) & ~WsExAppWindow);
        Reparent(window, desktop);
        Place(window, desktop, new(r.Left, r.Top, r.Right, r.Bottom));
    }
    public void Restore(IntPtr window, WindowsWindowState original)
    {
        var isChild = (original.Style & WsChild) != 0;
        var parent = isChild && IsWindow(original.Parent) ? original.Parent : IntPtr.Zero;
        Reparent(window, parent);
        // If an original child parent is gone, fall back to a usable top-level window.
        var style = isChild && parent == IntPtr.Zero ? (original.Style & ~WsChild) | WsPopup : original.Style;
        SetStyle(window, -16, style);
        SetStyle(window, -20, original.ExtendedStyle);
        if (!isChild)
        {
            var owner = IsWindow(original.Owner) ? original.Owner : IntPtr.Zero;
            // SetParent(..., NULL) normally already removes ownership. In particular, do not
            // ask SetWindowLongPtr to resolve a NULL owner when no owner change is needed.
            if (GetWindow(window, 4) != owner) SetStyle(window, -8, owner.ToInt64()); // GWLP_HWNDPARENT
        }
        Place(window, parent, original.Bounds);
    }
    private static void Place(IntPtr window, IntPtr parent, WindowRect rect)
    {
        var point = new Point { X = rect.Left, Y = rect.Top };
        if (parent != IntPtr.Zero && !ScreenToClient(parent, ref point)) throw new Win32Exception();
        // FRAMECHANGED | NOACTIVATE | NOZORDER: no focus theft or unrelated z-order changes.
        if (!SetWindowPos(window, IntPtr.Zero, point.X, point.Y, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x34))
            throw new Win32Exception();
    }
    private static void Reparent(IntPtr window, IntPtr parent)
    {
        // Clear the OS error as required by SetParent/GetWindowLongPtr/SetWindowLongPtr.
        // Marshal.SetLastPInvokeError only updates the managed cached value.
        ClearNativeError(0);
        var previous = SetParent(window, parent);
        var error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0) throw new Win32Exception(error);
    }
    private static long GetStyle(IntPtr window, int index)
    {
        // Clear the OS error as required by SetParent/GetWindowLongPtr/SetWindowLongPtr.
        // Marshal.SetLastPInvokeError only updates the managed cached value.
        ClearNativeError(0);
        var value = IntPtr.Size == 8 ? GetWindowLongPtr(window, index) : new IntPtr(GetWindowLong(window, index));
        var error = Marshal.GetLastPInvokeError();
        if (value == IntPtr.Zero && error != 0) throw new Win32Exception(error);
        return value.ToInt64();
    }
    private static void SetStyle(IntPtr window, int index, long value)
    {
        // Clear the OS error as required by SetParent/GetWindowLongPtr/SetWindowLongPtr.
        // Marshal.SetLastPInvokeError only updates the managed cached value.
        ClearNativeError(0);
        var previous = IntPtr.Size == 8 ? SetWindowLongPtr(window, index, new IntPtr(value)) : new IntPtr(SetWindowLong(window, index, unchecked((int)value)));
        var error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0)
            throw new Win32Exception(error, $"SetWindowLongPtr(index={index}, value={value}) failed (Win32 {error}).");
    }
    [DllImport("kernel32.dll", EntryPoint = "SetLastError")] private static extern void ClearNativeError(uint error);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "IsWindow")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool NativeIsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? name);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ScreenToClient(IntPtr window, ref Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(IntPtr window, int index, int value);
}

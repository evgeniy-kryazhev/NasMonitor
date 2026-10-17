using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace NasMonitor.App.Platform;

/// <summary>
/// Experimental Windows Explorer WorkerW host. This uses undocumented Explorer internals.
/// Do not treat a successful return as proof that desktop behavior is correct.
/// </summary>
public sealed class WindowsExplorerDesktopHost
{
    private IntPtr _windowHandle;
    private IntPtr _originalParent;

    public bool TryAttach(Window window, out string reason)
    {
        if (!OperatingSystem.IsWindows())
        {
            reason = "Only supported on Windows.";
            return false;
        }

        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle is null || platformHandle.Handle == IntPtr.Zero)
        {
            reason = "Native window handle unavailable.";
            return false;
        }

        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            reason = "Explorer Progman window unavailable.";
            return false;
        }

        // Ask Explorer to create the desktop WorkerW layout (undocumented).
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), IntPtr.Zero,
            0x0002, 1000, out _);

        IntPtr target = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                target = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (target == IntPtr.Zero)
        {
            reason = "Desktop WorkerW not found; no changes made.";
            return false;
        }

        var native = platformHandle.Handle;
        SetLastError(0);
        var previous = SetParent(native, target);
        if (previous == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
        {
            reason = $"SetParent failed: Win32 {Marshal.GetLastWin32Error()}";
            return false;
        }

        _windowHandle = native;
        _originalParent = previous;
        reason = "WorkerW parent assigned; desktop behavior requires manual verification.";
        return true;
    }

    public void Detach()
    {
        if (_windowHandle == IntPtr.Zero) return;
        SetParent(_windowHandle, _originalParent);
        _windowHandle = IntPtr.Zero;
        _originalParent = IntPtr.Zero;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? name);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("kernel32.dll")]
    private static extern void SetLastError(uint error);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, IntPtr lparam,
        uint flags, uint timeout, out IntPtr result);
}

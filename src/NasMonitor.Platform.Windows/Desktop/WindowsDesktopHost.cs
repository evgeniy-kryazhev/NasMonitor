using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using NasMonitor.Hosting.Abstractions;
using NasMonitor.Hosting.Models;

namespace NasMonitor.Platform.Windows.Desktop;

/// <summary>Experimental Explorer WorkerW host, not a supported shell extension API.</summary>
public sealed partial class WindowsDesktopHost : IDesktopWidgetHost, IDesktopPointerSource
{
    private readonly Dictionary<nint, (nint Parent, nint Style, nint ExtendedStyle)> originals = new();
    private nint desktopParent;

    /// <inheritdoc />
    public bool TryReadPointer(out DesktopPointer pointer)
    {
        pointer = default;
        if (!GetCursorPos(out Point position))
        {
            return false;
        }

        pointer = new DesktopPointer(position.X, position.Y, (GetAsyncKeyState(0x01) & 0x8000) != 0);
        return true;
    }

    /// <inheritdoc />
    public bool CanStartDrag(NativeWidget widget, DesktopPointer pointer)
    {
        var point = new Point { X = pointer.X, Y = pointer.Y };
        nint target = WindowFromPoint(point);
        if (target == widget.Handle || IsChild(widget.Handle, target))
        {
            return true;
        }

        var className = new StringBuilder(256);
        GetClassNameW(target, className, className.Capacity);
        string targetClass = className.ToString();
        if (targetClass is "WorkerW" or "Progman")
        {
            return target == desktopParent || IsChild(target, desktopParent);
        }

        GetWindowThreadProcessId(target, out uint targetProcess);
        GetWindowThreadProcessId(desktopParent, out uint desktopProcess);
        var parentClass = new StringBuilder(256);
        GetClassNameW(GetParent(target), parentClass, parentClass.Capacity);
        if (targetClass != "SysListView32" || targetProcess != desktopProcess ||
            parentClass.ToString() != "SHELLDLL_DefView")
        {
            return false;
        }

        // Explorer's full-screen icon ListView also covers empty desktop areas.
        // Use accessibility hit-testing rather than passing pointers into Explorer memory.
        object? accessible = null;
        try
        {
            int result = AccessibleObjectFromPoint(point, out accessible, out object child);
            if (result != 0 || accessible is null)
            {
                return false;
            }

            object? role = accessible.GetType().InvokeMember("accRole",
                BindingFlags.GetProperty,
                null,
                accessible,
                [child]);
            return role is int roleId && roleId is 0x21 or 0x0A;
        }
        catch (Exception exception) when (exception is COMException or TargetInvocationException
                                              or MissingMethodException)
        {
            return false;
        }
        finally
        {
            if (OperatingSystem.IsWindows() && accessible is not null && Marshal.IsComObject(accessible))
            {
                Marshal.ReleaseComObject(accessible);
            }
        }
    }

    /// <inheritdoc />
    public DesktopAttachResult Attach(NativeWidget widget,
        NativeBounds bounds,
        bool locked,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (widget.Descriptor != "HWND" || !IsWindow(widget.Handle))
        {
            return new DesktopAttachResult(false, "windows.invalid-hwnd");
        }

        try
        {
            if (!IsWindow(desktopParent))
            {
                desktopParent = FindDesktopParent();
            }

            if (desktopParent == 0)
            {
                return new DesktopAttachResult(false, "windows.workerw-unavailable");
            }

            if (!originals.ContainsKey(widget.Handle))
            {
                originals.Add(widget.Handle,
                    (GetParent(widget.Handle), GetWindowLongPtrW(widget.Handle, -16),
                        GetWindowLongPtrW(widget.Handle, -20)));
            }

            (IntPtr Parent, IntPtr Style, IntPtr ExtendedStyle) original = originals[widget.Handle];
            // SetParent does not adjust WS_CHILD/WS_POPUP itself.
            SetStyle(widget.Handle, -16, (original.Style.ToInt64() & ~0x80000000L) | 0x40000000L);
            long extendedStyle = original.ExtendedStyle.ToInt64() | 0x08000000L | 0x80L;
            if (locked)
            {
                extendedStyle |= 0x20L;
            }

            SetStyle(widget.Handle, -20, extendedStyle);
            if (GetParent(widget.Handle) != desktopParent)
            {
                Marshal.SetLastPInvokeError(0);
                nint previousParent = SetParent(widget.Handle, desktopParent);
                if (previousParent == 0 && Marshal.GetLastPInvokeError() != 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
            }

            var point = new Point { X = bounds.X, Y = bounds.Y };
            if (!ScreenToClient(desktopParent, ref point))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (!SetWindowPos(widget.Handle, 0, point.X, point.Y, bounds.Width, bounds.Height, 0x10 | 0x04 | 0x20))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return new DesktopAttachResult(GetParent(widget.Handle) == desktopParent,
                "windows.workerw-attached-experimental");
        }
        catch (Win32Exception exception)
        {
            return new DesktopAttachResult(false, $"windows.native-error-{exception.NativeErrorCode}");
        }
    }

    /// <inheritdoc />
    public bool IsAlive(NativeWidget widget)
    {
        return IsWindow(widget.Handle) && IsWindow(desktopParent) && GetParent(widget.Handle) == desktopParent;
    }

    /// <inheritdoc />
    public void Detach(NativeWidget widget)
    {
        if (!originals.Remove(widget.Handle, out (IntPtr Parent, IntPtr Style, IntPtr ExtendedStyle) original) ||
            !IsWindow(widget.Handle))
        {
            return;
        }

        SetParent(widget.Handle, original.Parent);
        SetWindowLongPtrW(widget.Handle, -16, original.Style);
        SetWindowLongPtrW(widget.Handle, -20, original.ExtendedStyle);
    }

    private static void SetStyle(nint handle, int index, long style)
    {
        Marshal.SetLastPInvokeError(0);
        nint previous = SetWindowLongPtrW(handle, index, (nint)style);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private static nint FindDesktopParent()
    {
        nint progman = FindWindowW("Progman", null);
        if (progman == 0)
        {
            return 0;
        }

        // Undocumented Explorer message. A timeout prevents a hung shell freezing the UI.
        SendMessageTimeoutW(progman, 0x052C, 0, 0, 0x02, 750, out _);
        nint worker = 0;
        EnumWindows((window, _) =>
            {
                if (FindWindowExW(window, 0, "SHELLDLL_DefView", null) != 0)
                {
                    worker = FindWindowExW(0, window, "WorkerW", null);
                    return worker == 0;
                }

                return true;
            },
            0);
        if (worker == 0 && FindWindowExW(progman, 0, "SHELLDLL_DefView", null) != 0)
        {
            // Observed on Windows build 26200: WorkerW is a child of Progman.
            // Do not use unrelated top-level WorkerW windows or Progman itself.
            worker = FindWindowExW(progman, 0, "WorkerW", null);
        }

        return worker;
    }
}

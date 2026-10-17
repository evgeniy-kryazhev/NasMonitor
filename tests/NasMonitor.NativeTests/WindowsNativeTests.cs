using System.ComponentModel;
using System.Runtime.InteropServices;
using NasMonitor.App.Platform;
using Xunit;

namespace NasMonitor.NativeTests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires real Windows user32; not a desktop acceptance test.";
    }
}

public sealed class WindowsNativeTests
{
    [WindowsFact]
    public void OwnedTopLevelWindowRestoresOwnerStylesAndScreenGeometry()
    {
        using var owner = NativeWindow.Create(0, 120, 150);
        using var parent = NativeWindow.Create(0, 300, 350);
        using var widget = NativeWindow.Create(owner.Handle, 450, 475);
        var api = new Win32DesktopApi();
        var original = api.Capture(widget.Handle);
        Assert.Equal(IntPtr.Zero, original.Parent);
        Assert.Equal(owner.Handle, original.Owner);
        try
        {
            api.Attach(widget.Handle, parent.Handle, original);
            var attached = api.Capture(widget.Handle);
            Assert.Equal(parent.Handle, attached.Parent);
            Assert.NotEqual(0, attached.Style & 0x40000000);
            Assert.Equal(0, attached.Style & 0x80000000);
            Assert.Equal(original.Bounds, attached.Bounds);
        }
        finally { api.Restore(widget.Handle, original); }
        Assert.Equal(original, api.Capture(widget.Handle));
    }

    [WindowsFact]
    public void UnownedWindowDoesNotAcquireDesktopAsOwnerAfterDetach()
    {
        using var parent = NativeWindow.Create(0, 200, 230);
        using var widget = NativeWindow.Create(0, 400, 440);
        var api = new Win32DesktopApi();
        var original = api.Capture(widget.Handle);
        for (var i = 0; i < 3; i++)
        {
            try { api.Attach(widget.Handle, parent.Handle, original); }
            finally { api.Restore(widget.Handle, original); }
            Assert.Equal(original, api.Capture(widget.Handle));
        }
    }

    [WindowsFact]
    public void InvalidParentFailureCanRestoreStylesAndBounds()
    {
        using var widget = NativeWindow.Create(0, 400, 440);
        var api = new Win32DesktopApi();
        var original = api.Capture(widget.Handle);
        try { Assert.Throws<Win32Exception>(() => api.Attach(widget.Handle, new IntPtr(123), original)); }
        finally { api.Restore(widget.Handle, original); }
        Assert.Equal(original, api.Capture(widget.Handle));
    }

    // Built-in STATIC windows isolate the native primitives from Explorer and never replace its windows.
    private sealed class NativeWindow(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;
        public static NativeWindow Create(IntPtr owner, int x, int y)
        {
            var handle = CreateWindowEx(0, "STATIC", "NAS Monitor native test", 0x80000000,
                x, y, 220, 180, owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (handle == IntPtr.Zero) throw new Win32Exception();
            return new(handle);
        }
        public void Dispose() => DestroyWindow(Handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string name, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    }
}

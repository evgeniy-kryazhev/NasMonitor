using System.Runtime.InteropServices;
using NasMonitor.Hosting;

namespace NasMonitor.Platform.MacOS;

/// <summary>Public AppKit desktop-level placement. Must be invoked on the main thread.</summary>
public sealed class MacDesktopHost : IDesktopWidgetHost
{
    private readonly Dictionary<nint, (nint Level, nuint Behavior, bool IgnoresMouse)> originals = new();

    /// <inheritdoc />
    public DesktopAttachResult Attach(NativeWidget widget, NativeBounds bounds, bool locked, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        nint window = GetWindow(widget);
        if (window == 0)
        {
            return new(false, "macos.unsupported-native-handle");
        }
        if (!originals.ContainsKey(window))
        {
            originals.Add(window, (Send(window, Selector("level")), (nuint)Send(window, Selector("collectionBehavior")), Send(window, Selector("ignoresMouseEvents")) != 0));
        }
        // kCGDesktopWindowLevelKey = 2; +1 sits above wallpaper, below desktop icons.
        nint desktopLevel = CGWindowLevelForKey(2) + 1;
        SendInteger(window, Selector("setLevel:"), desktopLevel);
        SendInteger(window, Selector("setCollectionBehavior:"), (nint)((1u << 0) | (1u << 4) | (1u << 6)));
        SendBool(window, Selector("setIgnoresMouseEvents:"), locked);
        SendBool(window, Selector("setHidesOnDeactivate:"), false);
        return new(Send(window, Selector("level")) == desktopLevel, "macos.desktop-level-applied-unverified");
    }

    /// <inheritdoc />
    public bool IsAlive(NativeWidget widget)
    {
        nint window = GetWindow(widget);
        return window != 0 && Send(window, Selector("level")) == CGWindowLevelForKey(2) + 1;
    }

    /// <inheritdoc />
    public void Detach(NativeWidget widget)
    {
        nint window = GetWindow(widget);
        if (window != 0 && originals.Remove(window, out var original))
        {
            SendInteger(window, Selector("setLevel:"), original.Level);
            SendInteger(window, Selector("setCollectionBehavior:"), (nint)original.Behavior);
            SendBool(window, Selector("setIgnoresMouseEvents:"), original.IgnoresMouse);
        }
    }

    private static nint GetWindow(NativeWidget widget)
    {
        return widget.Descriptor switch
        {
            "NSWindow" => widget.Handle,
            "NSView" when widget.Handle != 0 => Send(widget.Handle, Selector("window")),
            _ => 0
        };
    }

    private static nint Selector(string name) => sel_registerName(name);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern nint sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void SendInteger(nint receiver, nint selector, nint value);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void SendBool(nint receiver, nint selector, [MarshalAs(UnmanagedType.I1)] bool value);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] private static extern int CGWindowLevelForKey(int key);
}

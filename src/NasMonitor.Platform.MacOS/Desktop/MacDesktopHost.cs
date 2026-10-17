using NasMonitor.Hosting.Abstractions;
using NasMonitor.Hosting.Models;

namespace NasMonitor.Platform.MacOS.Desktop;

/// <summary>Public AppKit desktop-level placement. Must be invoked on the main thread.</summary>
public sealed partial class MacDesktopHost : IDesktopWidgetHost
{
    private readonly Dictionary<nint, (nint Level, nuint Behavior, bool IgnoresMouse)> originals = new();

    /// <inheritdoc />
    public DesktopAttachResult Attach(NativeWidget widget,
        NativeBounds bounds,
        bool locked,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        nint window = GetWindow(widget);
        if (window == 0)
        {
            return new DesktopAttachResult(false, "macos.unsupported-native-handle");
        }

        if (!originals.ContainsKey(window))
        {
            originals.Add(window,
                (Send(window, Selector("level")), (nuint)Send(window, Selector("collectionBehavior")),
                    Send(window, Selector("ignoresMouseEvents")) != 0));
        }

        // kCGDesktopWindowLevelKey = 2; +1 sits above wallpaper, below desktop icons.
        nint desktopLevel = CGWindowLevelForKey(2) + 1;
        SendInteger(window, Selector("setLevel:"), desktopLevel);
        SendInteger(window, Selector("setCollectionBehavior:"), (nint)((1u << 0) | (1u << 4) | (1u << 6)));
        SendBool(window, Selector("setIgnoresMouseEvents:"), locked);
        SendBool(window, Selector("setHidesOnDeactivate:"), false);
        return new DesktopAttachResult(Send(window, Selector("level")) == desktopLevel,
            "macos.desktop-level-applied-unverified");
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
        if (window != 0 && originals.Remove(window, out (IntPtr Level, UIntPtr Behavior, bool IgnoresMouse) original))
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

    private static nint Selector(string name)
    {
        return sel_registerName(name);
    }
}

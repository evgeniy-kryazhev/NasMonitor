using System.ComponentModel;
using NasMonitor.App.Platform;
using Xunit;

namespace NasMonitor.App.Tests;

public sealed class DesktopHostTests
{
    [Fact]
    public void RepeatedAttachPreservesOriginalSnapshot()
    {
        var api = new WindowsApi();
        using var host = new WindowsExplorerDesktopHost(api);
        Assert.Equal(HostState.Attached, host.Attach(1).State);
        Assert.Equal(HostState.Attached, host.Attach(1).State);
        Assert.Equal(HostState.Detached, host.Detach().State);
        Assert.Equal(1, api.Captures);
        Assert.Equal(api.Original, api.Restored);
    }

    [Fact]
    public void MissingWorkerDoesNotMutateWindow()
    {
        var api = new WindowsApi { Desktop = 0 };
        using var host = new WindowsExplorerDesktopHost(api);
        Assert.Equal(HostState.Unavailable, host.Attach(1).State);
        Assert.Equal(0, api.Captures);
        Assert.Equal(0, api.Attaches);
    }

    [Fact]
    public void PartialAttachmentFailureRollsBack()
    {
        var api = new WindowsApi { FailAttach = true };
        using var host = new WindowsExplorerDesktopHost(api);
        Assert.Equal(HostState.Unavailable, host.Attach(1).State);
        Assert.Equal(api.Original, api.Restored);
        Assert.Equal(HostState.Detached, host.Check().State);
    }

    [Fact]
    public void FailedRollbackRetainsSnapshotForRetry()
    {
        var api = new WindowsApi { FailAttach = true, FailRestore = true };
        using var host = new WindowsExplorerDesktopHost(api);
        host.Attach(1);
        Assert.Equal(HostState.Unavailable, host.Detach().State);
        api.FailRestore = false;
        Assert.Equal(HostState.Detached, host.Detach().State);
        Assert.Equal(api.Original, api.Restored);
    }

    [Fact]
    public void ReplacedExplorerParentIsRediscoveredWithoutRecapturingState()
    {
        var api = new WindowsApi();
        using var host = new WindowsExplorerDesktopHost(api);
        host.Attach(1);
        api.Desktop = 3;
        api.Parent = 0;
        Assert.Equal(HostState.Attached, host.Check().State);
        Assert.Equal((IntPtr)3, api.Parent);
        Assert.Equal(1, api.Captures);
        host.Detach();
        Assert.Equal(api.Original, api.Restored);
    }

    [Fact]
    public void ExplorerUnavailableRestoresWindowAndRetriesLater()
    {
        var api = new WindowsApi();
        using var host = new WindowsExplorerDesktopHost(api);
        host.Attach(1);
        api.Desktop = 0;
        api.Parent = 0;
        Assert.Equal(HostState.Unavailable, host.Check().State);
        Assert.Equal(api.Original, api.Restored);
        api.Desktop = 3;
        Assert.Equal(HostState.Attached, host.Check().State);
        Assert.Equal(1, api.Captures);
    }

    [Fact]
    public void DestroyedWidgetRequestsRecreationAndNeverRestoresDeadHandle()
    {
        var api = new WindowsApi();
        using var host = new WindowsExplorerDesktopHost(api);
        host.Attach(1);
        api.WidgetAlive = false;
        Assert.Equal(HostState.WindowLost, host.Check().State);
        Assert.Equal(HostState.Detached, host.Detach().State);
        Assert.Null(api.Restored);
    }

    [Fact]
    public void CannotAttachSecondWindowUntilFirstDetached()
    {
        var api = new WindowsApi();
        using var host = new WindowsExplorerDesktopHost(api);
        host.Attach(1);
        Assert.Equal(HostState.Unavailable, host.Attach(4).State);
        Assert.Equal(1, api.Attaches);
    }

    [Fact]
    public void DisposeRestoresOnce()
    {
        var api = new WindowsApi();
        var host = new WindowsExplorerDesktopHost(api);
        host.Attach(1);
        host.Dispose();
        host.Dispose();
        Assert.Equal(1, api.Restores);
    }

    [Fact]
    public void MacRepeatedAttachAndChecksRetainOriginalLevelAndSpacesPolicy()
    {
        var api = new MacApi();
        using var host = new MacOSDesktopHost(api);
        Assert.Equal(HostState.Attached, host.Attach(1).State);
        host.Attach(1);
        host.Check();
        host.Detach();
        host.Detach();
        Assert.Equal(1, api.Captures);
        Assert.Equal(new MacWindowState(0, 8), api.Restored);
        Assert.Equal(1, api.Restores);
    }

    [Fact]
    public void MacRejectsNullHandleAndSecondWindow()
    {
        var api = new MacApi();
        using var host = new MacOSDesktopHost(api);
        Assert.Equal(HostState.Unavailable, host.Attach(0).State);
        host.Attach(1);
        Assert.Equal(HostState.Unavailable, host.Attach(2).State);
        Assert.Equal(1, api.Captures);
    }

    [Fact]
    public void UnsupportedHostNeverReportsAttachment()
    {
        using var host = new UnsupportedDesktopHost();
        Assert.Equal(HostState.Unavailable, host.Attach(1).State);
        Assert.Equal(HostState.Detached, host.Check().State);
    }

    private sealed class MacApi : IMacDesktopApi
    {
        public int Captures, Restores;
        public MacWindowState? Restored;
        public MacWindowState Capture(IntPtr window) { Captures++; return new(0, 8); }
        public void Apply(IntPtr window) { }
        public void Restore(IntPtr window, MacWindowState state) { Restored = state; Restores++; }
    }

    private sealed class WindowsApi : IWindowsDesktopApi
    {
        public IntPtr Desktop = 2, Parent;
        public bool WidgetAlive = true, FailAttach, FailRestore;
        public int Captures, Attaches, Restores;
        public readonly WindowsWindowState Original = new(0, 0x80CF0000, 0x40000, new(-1800, 100, -1485, 480));
        public WindowsWindowState? Restored;
        public bool IsWindow(IntPtr window) => window == 1 ? WidgetAlive : window != IntPtr.Zero && window == Desktop;
        public IntPtr FindDesktop() => Desktop;
        public IntPtr ParentOf(IntPtr window) => Parent;
        public WindowsWindowState Capture(IntPtr window) { Captures++; return Original; }
        public void Attach(IntPtr window, IntPtr desktop, WindowsWindowState original)
        {
            Attaches++;
            Parent = desktop;
            if (FailAttach) throw new Win32Exception(5);
        }
        public void Restore(IntPtr window, WindowsWindowState original)
        {
            if (FailRestore) throw new Win32Exception(5);
            Restores++;
            Restored = original;
            Parent = original.Parent;
        }
    }
}

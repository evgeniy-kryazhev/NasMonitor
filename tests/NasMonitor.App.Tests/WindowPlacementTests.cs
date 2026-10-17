using Avalonia;
using Xunit;

namespace NasMonitor.App.Tests;

public sealed class WindowPlacementTests
{
    private static readonly PixelRect Primary = new(0, 40, 1920, 1040);
    private static readonly PixelSize Widget = new(315, 380);

    [Fact]
    public void VisibleWindowOnNegativeCoordinateMonitorIsUnchanged()
    {
        var position = new PixelPoint(-1700, 150);
        Assert.Equal(position, WindowPlacement.KeepReachable(position, Widget,
            [Primary, new PixelRect(-1920, 0, 1920, 1080)]));
    }

    [Fact]
    public void RemovedLeftMonitorReturnsWindowToPrimaryWorkArea()
    {
        Assert.Equal(new PixelPoint(0, 150),
            WindowPlacement.KeepReachable(new(-1700, 150), Widget, [Primary]));
    }

    [Fact]
    public void WindowBelowScreenIsMovedUpAndRespectsTaskbarWorkArea()
    {
        Assert.Equal(new PixelPoint(400, 700),
            WindowPlacement.KeepReachable(new(400, 3000), Widget, [Primary]));
    }

    [Fact]
    public void VisibleBottomOfWindowWithoutTitleStripDoesNotCountAsReachable()
    {
        Assert.Equal(new PixelPoint(400, 40),
            WindowPlacement.KeepReachable(new(400, -200), Widget, [Primary]));
    }

    [Fact]
    public void NearestWorkAreaWinsWhenWindowIsBetweenSeparatedMonitors()
    {
        Assert.Equal(new PixelPoint(2500, 100), WindowPlacement.KeepReachable(
            new(2200, 100), Widget, [Primary, new PixelRect(2500, 0, 1920, 1080)]));
    }

    [Fact]
    public void PartiallyVisibleDragStripIsPreservedButAccountsForDisplayScale()
    {
        var position = new PixelPoint(1820, 100);
        Assert.Equal(position, WindowPlacement.KeepReachable(position, Widget, [Primary]));
        Assert.Equal(new PixelPoint(1605, 100),
            WindowPlacement.KeepReachable(position, Widget, [Primary], scale: 2));
    }

    [Fact]
    public void OversizedWindowAlignsWithWorkAreaOriginWithoutResizing()
    {
        Assert.Equal(new PixelPoint(0, 40), WindowPlacement.KeepReachable(
            new(4000, 3000), new(3000, 2000), [Primary]));
    }

    [Fact]
    public void MissingOrTransientEmptyScreenInformationDoesNotMoveWindow()
    {
        var position = new PixelPoint(4000, 3000);
        Assert.Equal(position, WindowPlacement.KeepReachable(position, Widget, []));
        Assert.Equal(position, WindowPlacement.KeepReachable(position, Widget, [new(0, 0, 0, 0)]));
    }
}

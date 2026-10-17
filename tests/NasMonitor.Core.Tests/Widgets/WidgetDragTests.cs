using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.Core.Widgets;

namespace NasMonitor.Core.Tests.Widgets;

[TestClass]
public sealed class WidgetDragTests
{
    [TestMethod]
    public void DragUsesPhysicalOffsetAcrossNegativeAndScaledDisplays()
    {
        var initial = new WidgetLayout(80, 100, 300, 210, false);
        var drag = new WidgetDragSession(initial, 110, 120);
        DisplayArea[] displays =
        [
            new(0, 0, 1920, 1080, 1),
            new(-1920, 0, 1920, 1080, 2)
        ];

        WidgetLayout moved = drag.MoveTo(-900, 320, displays);

        Assert.AreEqual(new WidgetLayout(-930, 300, 300, 210, false), moved);
    }

    [TestMethod]
    public void ReturningFromClampedEdgeDoesNotAccumulatePositionError()
    {
        var initial = new WidgetLayout(80, 100, 300, 210, false);
        var drag = new WidgetDragSession(initial, 100, 120);
        DisplayArea[] displays = [new(0, 0, 1920, 1080, 1.5)];

        WidgetLayout clamped = drag.MoveTo(2000, 2000, displays);
        WidgetLayout returned = drag.MoveTo(100, 120, displays);

        Assert.AreEqual(1470, clamped.X);
        Assert.AreEqual(765, clamped.Y);
        Assert.AreEqual(initial, returned);
    }

    [TestMethod]
    public void LostMonitorDuringDragFallsBackToAvailableWorkArea()
    {
        var initial = new WidgetLayout(-1800, 100, 300, 210, false);
        var drag = new WidgetDragSession(initial, -1780, 120);

        WidgetLayout moved = drag.MoveTo(-1700, 200, [new DisplayArea(0, 0, 1920, 1080, 1)]);

        Assert.AreEqual(0, moved.X);
        Assert.AreEqual(180, moved.Y);
        Assert.IsFalse(moved.Locked);
    }
}

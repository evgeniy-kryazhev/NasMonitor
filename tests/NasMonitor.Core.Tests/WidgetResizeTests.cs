using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.Core;

namespace NasMonitor.Core.Tests;

[TestClass]
public sealed class WidgetResizeTests
{
    [TestMethod]
    public void PhysicalDeltaIsConvertedToDipAtFractionalScale()
    {
        var layout = new WidgetLayout(-1800, 100, 300, 210, false);
        var resize = new WidgetResizeSession(layout, -1360, 400, 1.5);

        WidgetLayout resized = resize.ResizeTo(-1210, 490, [new(-1920, 0, 1920, 1080, 1.5)]);

        Assert.AreEqual(layout with { Width = 400, Height = 270 }, resized);
    }

    [TestMethod]
    public void ScreenEdgeLimitsSizeWithoutMovingTopLeftCorner()
    {
        var layout = new WidgetLayout(1400, 700, 300, 210, false);
        var resize = new WidgetResizeSession(layout, 1690, 900, 1);

        WidgetLayout resized = resize.ResizeTo(3000, 2000, [new(0, 0, 1920, 1080, 1)]);

        Assert.AreEqual(layout with { Width = 520, Height = 380 }, resized);
    }

    [TestMethod]
    public void ReturningAfterSizeLimitsRestoresInitialDimensions()
    {
        var layout = new WidgetLayout(80, 100, 300, 210, false);
        var resize = new WidgetResizeSession(layout, 370, 300, 1);
        DisplayArea[] displays = [new(0, 0, 1920, 1080, 1)];

        WidgetLayout minimum = resize.ResizeTo(-1000, -1000, displays);
        WidgetLayout maximum = resize.ResizeTo(5000, 5000, displays);
        WidgetLayout returned = resize.ResizeTo(370, 300, displays);

        Assert.AreEqual(260.0, minimum.Width);
        Assert.AreEqual(72.0, minimum.Height);
        Assert.AreEqual(720.0, maximum.Width);
        Assert.AreEqual(600.0, maximum.Height);
        Assert.AreEqual(layout, returned);
    }

    [TestMethod]
    public void DisconnectedMonitorAndSmallWorkAreaRemainVisible()
    {
        var layout = new WidgetLayout(-1800, 100, 300, 210, false);
        var resize = new WidgetResizeSession(layout, -1510, 300, 1);

        WidgetLayout resized = resize.ResizeTo(5000, 5000, [new(0, 0, 400, 300, 2)]);

        Assert.AreEqual(new WidgetLayout(0, 0, 200, 150, false), resized);
    }

    [TestMethod]
    public void GripUsesScaledBoundsAndIsDisabledWhenLocked()
    {
        var layout = new WidgetLayout(100, 100, 300, 210, false);

        Assert.IsTrue(WidgetResizeSession.IsOverGrip(layout, 690, 510, 2));
        Assert.IsFalse(WidgetResizeSession.IsOverGrip(layout, 650, 510, 2));
        Assert.IsFalse(WidgetResizeSession.IsOverGrip(layout, 700, 520, 2));
        Assert.IsFalse(WidgetResizeSession.IsOverGrip(layout with { Locked = true }, 690, 510, 2));
    }
}

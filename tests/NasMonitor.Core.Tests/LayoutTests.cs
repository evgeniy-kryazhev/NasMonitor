using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.Core;

namespace NasMonitor.Core.Tests;

[TestClass]
public sealed class LayoutTests
{
    [TestMethod]
    public void LegacyAutoHeightFieldDoesNotPreventManualSizing()
    {
        WidgetLayout legacy = System.Text.Json.JsonSerializer.Deserialize<WidgetLayout>(
            """{"X":80,"Y":100,"Width":300,"Height":210,"Locked":true,"AutoHeight":true}""")!;

        Assert.AreEqual(210.0, legacy.Height);
        Assert.AreEqual(96.0, (legacy with { Height = 96 }).Fit([]).Height);
    }

    [TestMethod]
    public void ContentMinimumPreservesLargerManualHeightAndRejectsClipping()
    {
        var layout = new WidgetLayout(80, 100, 300, 480);

        Assert.AreEqual(480.0, layout.WithContentMinimum(350).Height);
        Assert.AreEqual(350.0, (layout with { Height = 200 }).WithContentMinimum(350).Height);
        Assert.AreEqual(400.0, (layout with { Height = 400 }).WithContentMinimum(350).Height);
        Assert.AreEqual(100.0, (layout with { Height = 100 }).WithContentMinimum(94).Height);
    }

    [TestMethod]
    public void DisconnectedDisplayFallsBackToVisiblePrimaryArea()
    {
        var saved = new WidgetLayout(4000, -1000, 300, 210);
        var display = new DisplayArea(0, 0, 1920, 1080, 1.5);

        WidgetLayout restored = saved.Fit([display]);

        Assert.AreEqual(1470, restored.X);
        Assert.AreEqual(0, restored.Y);
        Assert.AreEqual(300.0, restored.Width);
    }

    [TestMethod]
    public void NegativeMonitorCoordinatesRemainValid()
    {
        var saved = new WidgetLayout(-1800, 100);
        DisplayArea[] displays = [new(0, 0, 1920, 1080, 1), new(-1920, 0, 1920, 1080, 1)];

        Assert.AreEqual(saved, saved.Fit(displays));
    }

    [TestMethod]
    public void InvalidAndOversizedDimensionsAreNormalized()
    {
        var saved = new WidgetLayout(0, 0, double.NaN, double.PositiveInfinity);
        WidgetLayout restored = saved.Fit([new(0, 0, 400, 300, 2)]);

        Assert.AreEqual(200.0, restored.Width);
        Assert.AreEqual(150.0, restored.Height);
    }

    [TestMethod]
    public void MissingScreensStillProduceSafeGeometry()
    {
        WidgetLayout restored = new WidgetLayout(int.MaxValue, int.MinValue, -5, -5).Fit([]);

        Assert.AreEqual(1660, restored.X);
        Assert.AreEqual(0, restored.Y);
        Assert.AreEqual(260.0, restored.Width);
        Assert.AreEqual(72.0, restored.Height);
    }

    [TestMethod]
    public void SyntheticStreamIsBoundedAndPreservesTimestamp()
    {
        var timestamp = DateTimeOffset.Parse("2026-10-17T00:00:00Z");
        for (int tick = 0; tick < 10_000; tick++)
        {
            DemoSnapshot sample = DemoSnapshot.Create(tick, timestamp);
            Assert.IsTrue(sample.Cpu is >= 0 and <= 100);
            Assert.IsTrue(sample.Ram is >= 0 and <= 100);
            Assert.AreEqual(timestamp, sample.CollectedAt);
        }
    }
}

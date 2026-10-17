using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.Core;

namespace NasMonitor.Core.Tests;

[TestClass]
public sealed class WidgetAppearanceTests
{
    [TestMethod]
    public void InvalidPersistedAppearanceFallsBackToVisibleValues()
    {
        var requested = new WidgetAppearance("  ", "not-a-color", double.NaN, double.PositiveInfinity,
            Theme: (WidgetTheme)999);

        WidgetAppearance normalized = requested.Normalize();

        Assert.AreEqual(new WidgetAppearance(), normalized);
    }

    [TestMethod]
    public void NumericLimitsKeepWidgetVisibleAndCornersBounded()
    {
        WidgetAppearance minimum = new WidgetAppearance(Opacity: -50, CornerRadius: -50).Normalize();
        WidgetAppearance maximum = new WidgetAppearance(Opacity: 50, CornerRadius: 500).Normalize();

        Assert.AreEqual(0.35, minimum.Opacity);
        Assert.AreEqual(0.0, minimum.CornerRadius);
        Assert.AreEqual(1.0, maximum.Opacity);
        Assert.AreEqual(32.0, maximum.CornerRadius);
    }

    [TestMethod]
    public void ValidCustomizationPreservesMetricSelectionAndTheme()
    {
        var requested = new WidgetAppearance("  Home NAS  ", "#aabbcc", 0.75, 20,
            ShowCpu: false, ShowRam: true, ShowLastUpdate: false, Theme: WidgetTheme.Dark);

        WidgetAppearance normalized = requested.Normalize();

        Assert.AreEqual(requested with { Title = "Home NAS", AccentColor = "#AABBCC" }, normalized);
    }

    [TestMethod]
    public void ColorsRequireExactlySixHexDigits()
    {
        Assert.AreEqual("#35B89A", new WidgetAppearance(AccentColor: "#12345g").Normalize().AccentColor);
        Assert.AreEqual("#35B89A", new WidgetAppearance(AccentColor: "#00112233").Normalize().AccentColor);
        Assert.AreEqual("#001122", new WidgetAppearance(AccentColor: " #001122 ").Normalize().AccentColor);
    }
}

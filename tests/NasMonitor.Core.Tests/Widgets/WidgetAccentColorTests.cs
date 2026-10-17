using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.Core.Widgets;

namespace NasMonitor.Core.Tests.Widgets;

[TestClass]
public sealed class WidgetAccentColorTests
{
    [TestMethod]
    public void NormalizesTrimmedRgbWithoutChangingItsComponents()
    {
        Assert.IsTrue(WidgetAccentColor.TryNormalize(" #aB09ef ", out string color));
        Assert.AreEqual("#AB09EF", color);
    }

    [TestMethod]
    public void RejectsPartialAlphaAndNonHexColors()
    {
        string?[] invalidColors = [null, "", "#7", "#ABC", "#00112233", "#12GG34", "red"];
        foreach (string? candidate in invalidColors)
        {
            Assert.IsFalse(WidgetAccentColor.TryNormalize(candidate, out string color), candidate);
            Assert.AreEqual("", color);
        }
    }
}

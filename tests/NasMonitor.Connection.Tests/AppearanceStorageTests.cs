using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.App;
using NasMonitor.Core;

namespace NasMonitor.Connection.Tests;

[TestClass]
public sealed class AppearanceStorageTests
{
    [TestMethod]
    public async Task AppearanceRoundTripDoesNotChangePlacement()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NasMonitor.Tests", Guid.NewGuid().ToString("N"));
        var storage = new SpikeStorage(directory);
        var placement = new WidgetLayout(-1200, 80, 400, 250, false);
        var appearance = new WidgetAppearance("Home NAS", "#799CE8", 0.7, 24,
            ShowCpu: false, Theme: WidgetTheme.Dark);
        try
        {
            Assert.IsTrue(await storage.SaveAsync(placement, CancellationToken.None));
            Assert.IsTrue(await storage.SaveAppearanceAsync(appearance, CancellationToken.None));

            Assert.AreEqual(placement, await storage.LoadAsync(CancellationToken.None));
            Assert.AreEqual(appearance, await storage.LoadAppearanceAsync(CancellationToken.None));
            Assert.IsTrue(await storage.SaveAppearanceAsync(new WidgetAppearance(), CancellationToken.None));
            Assert.AreEqual(placement, await storage.LoadAsync(CancellationToken.None));
        }
        finally
        {
            File.Delete(Path.Combine(directory, "layout.json"));
            File.Delete(Path.Combine(directory, "appearance.json"));
            Directory.Delete(directory);
        }
    }

    [TestMethod]
    public void AppliedAppearanceUpdatesSharedWidgetAndPreviewState()
    {
        var viewModel = new SpikeViewModel();

        viewModel.ApplyAppearance(new WidgetAppearance("Home NAS", "invalid", 0.6, 20,
            ShowCpu: false, ShowRam: false, ShowLastUpdate: false));

        Assert.AreEqual("Home NAS", viewModel.WidgetTitle);
        Assert.AreEqual(0.6, viewModel.SurfaceOpacity);
        Assert.IsFalse(viewModel.ShowCpu);
        Assert.IsFalse(viewModel.ShowRam);
        Assert.IsFalse(viewModel.ShowLastUpdate);
        Assert.AreEqual("#ff35b89a", viewModel.AccentBrush.ToString());
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.App.Persistence;
using NasMonitor.Core.Widgets;

namespace NasMonitor.Connection.Tests.Persistence;

[TestClass]
public sealed class WidgetCatalogTests
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "NasMonitor.Tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        string testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NasMonitor.Tests"));
        string resolved = Path.GetFullPath(directory);
        Assert.AreEqual(testRoot, Path.GetDirectoryName(resolved));
        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, true);
        }
    }

    [TestMethod]
    public async Task MissingCatalogPreservesLegacySettingsForMigration()
    {
        var storage = new WidgetSettingsStore(directory);
        var original = new WidgetLayout(-1200, 80, 400, 500, false);
        var nasId = Guid.NewGuid();
        await storage.SaveAsync(original, CancellationToken.None);
        await storage.SaveNasSelectionAsync(nasId, CancellationToken.None);

        Assert.IsNull(await storage.LoadCatalogAsync(CancellationToken.None));
        Assert.AreEqual(original, await storage.LoadAsync(CancellationToken.None));
        Assert.AreEqual(nasId, await storage.LoadNasSelectionAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task RestartRestoresMembershipSelectionAndIndependentWidgetSettings()
    {
        var storage = new WidgetSettingsStore(directory);
        var first = new WidgetCatalogEntry(Guid.NewGuid(), true);
        var second = new WidgetCatalogEntry(Guid.NewGuid());
        var catalog = new WidgetCatalog(1, [first, second], second.Id);
        WidgetSettingsStore other = storage.ForWidget(second.Id);
        var firstNas = Guid.NewGuid();
        var secondNas = Guid.NewGuid();
        var firstAppearance = new WidgetAppearance("Primary", "#112233");
        var secondAppearance = new WidgetAppearance("Backup", "#AABBCC", ShowCpu: false);
        var firstLayout = new WidgetLayout(100, 200, 350, 500);
        var secondLayout = new WidgetLayout(600, 100, 400, 550);
        await storage.SaveAppearanceAsync(firstAppearance, CancellationToken.None);
        await storage.SaveAsync(firstLayout, CancellationToken.None);
        await storage.SaveNasSelectionAsync(firstNas, CancellationToken.None);
        await other.SaveAppearanceAsync(secondAppearance, CancellationToken.None);
        await other.SaveAsync(secondLayout, CancellationToken.None);
        await other.SaveNasSelectionAsync(secondNas, CancellationToken.None);
        Assert.IsTrue(await storage.SaveCatalogAsync(catalog, CancellationToken.None));

        var restarted = new WidgetSettingsStore(directory);
        WidgetCatalog loaded = (await restarted.LoadCatalogAsync(CancellationToken.None))!;
        Assert.AreEqual(second.Id, loaded.SelectedWidgetId);
        CollectionAssert.AreEqual(catalog.Widgets, loaded.Widgets);
        Assert.AreEqual(firstAppearance, await restarted.LoadAppearanceAsync(CancellationToken.None));
        Assert.AreEqual(firstLayout, await restarted.LoadAsync(CancellationToken.None));
        Assert.AreEqual(firstNas, await restarted.LoadNasSelectionAsync(CancellationToken.None));
        WidgetSettingsStore restoredSecond = restarted.ForWidget(second.Id);
        Assert.AreEqual(secondAppearance, await restoredSecond.LoadAppearanceAsync(CancellationToken.None));
        Assert.AreEqual(secondLayout, await restoredSecond.LoadAsync(CancellationToken.None));
        Assert.AreEqual(secondNas, await restoredSecond.LoadNasSelectionAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task InvalidCatalogIsReportedWithoutOverwritingItsContents()
    {
        var storage = new WidgetSettingsStore(directory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "widgets.json");
        const string invalidJson = """{"SchemaVersion":999,"Widgets":[],"SelectedWidgetId":null}""";
        await File.WriteAllTextAsync(path, invalidJson);

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => storage.LoadCatalogAsync(CancellationToken.None));
        Assert.AreEqual(invalidJson, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task DuplicateIdentityCannotCreateTwoWindowsFromOneSettingsScope()
    {
        var storage = new WidgetSettingsStore(directory);
        var entry = new WidgetCatalogEntry(Guid.NewGuid());
        await storage.SaveCatalogAsync(new WidgetCatalog(1, [entry, entry], entry.Id), CancellationToken.None);

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => storage.LoadCatalogAsync(CancellationToken.None));
        Assert.ThrowsException<ArgumentException>(() => storage.ForWidget(Guid.Empty));
    }
}

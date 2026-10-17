using System.Diagnostics;
using System.Text.Json;
using NasMonitor.Core.Widgets;

namespace NasMonitor.App.Persistence;

/// <summary>Atomic non-secret settings and bounded diagnostic events.</summary>
public sealed class WidgetSettingsStore
{
    private readonly string directory;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    /// <summary>Use the normal per-user settings location.</summary>
    public WidgetSettingsStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NasMonitor",
        "DesktopSpike"))
    {
    }

    /// <summary>Use an explicit directory, including an isolated test directory.</summary>
    public WidgetSettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = directory;
    }

    /// <summary>Create an isolated settings scope without changing legacy filenames.</summary>
    public WidgetSettingsStore ForWidget(Guid widgetId)
    {
        if (widgetId == Guid.Empty)
        {
            throw new ArgumentException("widget.invalid-id", nameof(widgetId));
        }

        return new WidgetSettingsStore(Path.Combine(directory, "widgets", widgetId.ToString("N")));
    }

    /// <summary>Read the catalog; a missing file means a legacy single-widget installation.</summary>
    public async Task<WidgetCatalog?> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "widgets.json");
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            WidgetCatalog? catalog = JsonSerializer.Deserialize<WidgetCatalog>(json);
            if (catalog is null || catalog.SchemaVersion != 1 || catalog.Widgets is null ||
                catalog.Widgets.Count == 0 || catalog.Widgets.Any(entry => entry is null || entry.Id == Guid.Empty) ||
                catalog.Widgets.Select(entry => entry.Id).Distinct().Count() != catalog.Widgets.Count ||
                catalog.Widgets.Count(entry => entry.UsesLegacySettings) > 1)
            {
                throw new InvalidDataException("widgets.catalog-invalid");
            }

            return catalog;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("widgets.catalog-invalid", exception);
        }
    }

    /// <summary>Atomically save catalog membership and the selected widget.</summary>
    public Task<bool> SaveCatalogAsync(WidgetCatalog catalog, CancellationToken cancellationToken)
    {
        return SaveDocumentAsync("widgets.json", catalog, cancellationToken);
    }

    /// <summary>Read the widget's saved NAS identifier without credentials.</summary>
    public async Task<Guid?> LoadNasSelectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(Path.Combine(directory, "source.json"), cancellationToken);
            return JsonSerializer.Deserialize<Guid?>(json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Persist the widget's data source independently of appearance and placement.</summary>
    public Task<bool> SaveNasSelectionAsync(Guid? nasId, CancellationToken cancellationToken)
    {
        return SaveDocumentAsync("source.json", nasId, cancellationToken);
    }

    /// <summary>Read appearance without changing saved placement.</summary>
    public async Task<WidgetAppearance> LoadAppearanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(Path.Combine(directory, "appearance.json"), cancellationToken);
            return (JsonSerializer.Deserialize<WidgetAppearance>(json) ?? new WidgetAppearance()).Normalize();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log("appearance.default", exception.GetType().Name);
            return new WidgetAppearance();
        }
    }

    /// <summary>Persist normalized appearance using the same atomic write path as placement.</summary>
    public Task<bool> SaveAppearanceAsync(WidgetAppearance appearance, CancellationToken cancellationToken)
    {
        return SaveDocumentAsync("appearance.json", appearance.Normalize(), cancellationToken);
    }

    /// <summary>Read placement, falling back safely on missing or invalid settings.</summary>
    public async Task<WidgetLayout> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(Path.Combine(directory, "layout.json"), cancellationToken);
            return JsonSerializer.Deserialize<WidgetLayout>(json) ?? new WidgetLayout();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log("settings.default", exception.GetType().Name);
            return new WidgetLayout();
        }
    }

    /// <summary>Write to a sibling temporary file, then atomically replace.</summary>
    public Task<bool> SaveAsync(WidgetLayout layout, CancellationToken cancellationToken)
    {
        return SaveDocumentAsync("layout.json", layout, cancellationToken);
    }

    private async Task<bool> SaveDocumentAsync<T>(string fileName, T document, CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, fileName + ".tmp");
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(document), cancellationToken);
            File.Move(temporaryPath, Path.Combine(directory, fileName), true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log("settings.save-failed", exception.GetType().Name);
            return false;
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>Only controlled event codes and exception type names are accepted by callers.</summary>
    public void Log(string code, string detail = "")
    {
        try
        {
            Directory.CreateDirectory(directory);
            string logPath = Path.Combine(directory, "spike.log");
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 512_000)
            {
                File.Move(logPath, Path.Combine(directory, "spike.previous.log"), true);
            }

            File.AppendAllText(logPath, $"{DateTimeOffset.UtcNow:O} {code} {detail}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"diagnostics.unavailable {exception.GetType().Name}");
        }
    }
}

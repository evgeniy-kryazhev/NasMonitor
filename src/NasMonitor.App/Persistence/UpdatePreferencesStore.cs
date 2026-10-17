using System.Text.Json;

namespace NasMonitor.App.Persistence;

/// <summary>Persists only the automatic-check preference, separately from NAS credentials.</summary>
public sealed class UpdatePreferencesStore(string directory)
{
    private readonly SemaphoreSlim writes = new(1, 1);

    /// <summary>Read the saved choice; a missing or corrupt preference uses automatic checks.</summary>
    public async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(Path.Combine(directory, "preferences.json"), cancellationToken);
            return JsonSerializer.Deserialize<bool>(json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return true;
        }
    }

    /// <summary>Serialize preference changes and atomically replace the previous value.</summary>
    public async Task<bool> SaveAsync(bool automaticChecks, CancellationToken cancellationToken)
    {
        await writes.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, "preferences.json.tmp");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(automaticChecks), cancellationToken);
            File.Move(temporary, Path.Combine(directory, "preferences.json"), true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            writes.Release();
        }
    }

    /// <summary>Read only controlled status codes produced by the replacement helper.</summary>
    public async Task<string?> ReadInstallResultAsync(CancellationToken cancellationToken)
    {
        try
        {
            string result = await File.ReadAllTextAsync(Path.Combine(directory, "last-result.txt"), cancellationToken);
            return result is "UpdateInstalled" or "UpdateInstallFailed" ? result : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

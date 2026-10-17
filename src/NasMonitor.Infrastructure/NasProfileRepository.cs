using System.Text.Json;
using NasMonitor.Core;

namespace NasMonitor.Infrastructure;

/// <summary>Versioned, atomic profile persistence. No password or session fields exist in the schema.</summary>
public sealed class NasProfileRepository(string filePath) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Read profiles; a corrupt or newer document fails closed rather than being overwritten.</summary>
    public async Task<IReadOnlyList<NasProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Insert or update the profile identified by its generated ID.</summary>
    public Task UpsertAsync(NasProfile profile, CancellationToken cancellationToken)
    {
        profile.Validate();
        return ModifyAsync(profiles =>
        {
            profiles.RemoveAll(existing => existing.Id == profile.Id);
            profiles.Add(profile);
        }, cancellationToken);
    }

    /// <summary>Remove a profile from the non-secret configuration.</summary>
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        return ModifyAsync(profiles => profiles.RemoveAll(profile => profile.Id == id), cancellationToken);
    }

    private async Task ModifyAsync(Action<List<NasProfile>> modify, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = filePath + ".tmp";
        try
        {
            List<NasProfile> profiles = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
            modify(profiles);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            var document = new ProfileDocument(1, profiles);
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(document,
                new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<NasProfile>> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        ProfileDocument? document = JsonSerializer.Deserialize<ProfileDocument>(json);
        if (document is null || document.SchemaVersion != 1 || document.Profiles is null ||
            document.Profiles.Any(profile => profile is null) ||
            document.Profiles.Select(profile => profile.Id).Distinct().Count() != document.Profiles.Count)
        {
            throw new InvalidDataException("profiles.schema-invalid");
        }
        foreach (NasProfile profile in document.Profiles)
        {
            profile.Validate();
        }
        return document.Profiles;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        gate.Dispose();
    }

    private sealed record ProfileDocument(int SchemaVersion, List<NasProfile> Profiles);
}

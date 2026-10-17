using System.Text.Json;
using NasMonitor.Core;

namespace NasMonitor.App.Monitoring;

internal sealed class ProfileStore(string path)
{
    public static ProfileStore Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NasMonitor", "profiles.json"));
    public async Task<IReadOnlyList<NasProfile>> LoadAsync(CancellationToken token)
    {
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        var profiles = await JsonSerializer.DeserializeAsync<NasProfile[]>(stream, cancellationToken: token) ?? [];
        return profiles.Select(p => NasProfile.Create(p.Name, p.Endpoint.AbsoluteUri, p.Username, p.RememberCredentials, p.Id)).ToArray();
    }
    public async Task SaveAsync(IEnumerable<NasProfile> profiles, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(profiles.ToArray(), new JsonSerializerOptions { WriteIndented = true }), token);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

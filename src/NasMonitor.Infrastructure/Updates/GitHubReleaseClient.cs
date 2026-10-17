using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NasMonitor.Core.Updates;

namespace NasMonitor.Infrastructure.Updates;

/// <summary>Reads a public GitHub release feed without credentials or NAS HTTP configuration.</summary>
public sealed class GitHubReleaseClient(HttpClient client, string owner, string repository, string stagingDirectory)
    : IReleaseUpdateClient
{
    private const long MaximumExecutableSize = 512L * 1024 * 1024;

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion,
        string assetName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/releases/latest");
        request.Headers.UserAgent.ParseAdd("NasMonitor/" + currentVersion.ToString(3));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using HttpResponseMessage response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoReleases);
        }

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await ReadLimitedAsync(response, cancellationToken)
            .ConfigureAwait(false));
        JsonElement root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean() ||
            !ReleaseVersion.TryParse(root.GetProperty("tag_name").GetString(), out Version version))
        {
            throw new InvalidDataException("updates.invalid-release");
        }

        var normalizedCurrent =
            new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
        if (version <= normalizedCurrent)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
        }

        JsonElement[] assets = root.GetProperty("assets").EnumerateArray().ToArray();
        JsonElement[] matching = assets.Where(asset => asset.GetProperty("name").GetString() == assetName &&
                                                       asset.GetProperty("state").GetString() == "uploaded").ToArray();
        if (matching.Length != 1)
        {
            return new UpdateCheckResult(UpdateCheckStatus.MissingAsset);
        }

        JsonElement selected = matching[0];
        long size = selected.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaximumExecutableSize)
        {
            throw new InvalidDataException("updates.invalid-size");
        }

        string tag = root.GetProperty("tag_name").GetString()!;
        Uri download = ValidateDownloadUri(selected.GetProperty("browser_download_url").GetString(), tag, assetName);
        string? hash = selected.TryGetProperty("digest", out JsonElement digest)
            ? ParseDigest(digest.GetString())
            : null;
        Uri? checksumsUri = null;
        JsonElement[] checksums = assets.Where(asset => asset.GetProperty("name").GetString() == "SHA256SUMS.txt" &&
                                                        asset.GetProperty("state").GetString() == "uploaded").ToArray();
        if (checksums.Length == 1)
        {
            checksumsUri = ValidateDownloadUri(checksums[0].GetProperty("browser_download_url").GetString(),
                tag,
                "SHA256SUMS.txt");
        }

        if (hash is null && checksumsUri is null)
        {
            return new UpdateCheckResult(UpdateCheckStatus.MissingAsset);
        }

        string notes = root.TryGetProperty("body", out JsonElement body) ? body.GetString() ?? "" : "";
        if (notes.Length > 16000)
        {
            notes = notes[..16000];
        }

        var page = new Uri($"https://github.com/{owner}/{repository}/releases/tag/{Uri.EscapeDataString(tag)}");
        var release = new UpdateRelease(version, page, notes, assetName, download, size, hash, checksumsUri);
        return new UpdateCheckResult(UpdateCheckStatus.Available, release);
    }

    /// <inheritdoc />
    public async Task<DownloadedUpdate> DownloadAsync(UpdateRelease release,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        string expectedHash =
            release.Sha256 ?? await ReadChecksumAsync(release, cancellationToken).ConfigureAwait(false);
        string directory = Path.Combine(stagingDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string partial = Path.Combine(directory, "payload.partial");
        string payload = Path.Combine(directory, "payload.bin");
        try
        {
            using HttpResponseMessage response = await client.GetAsync(release.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            RequireHttps(response);
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
            {
                throw new InvalidDataException("updates.invalid-size");
            }

            await using Stream input =
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(partial,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             65536,
                             FileOptions.Asynchronous))
            {
                byte[] buffer = new byte[65536];
                long received = 0;
                int reportedPercent = -1;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    received += count;
                    if (received > release.Size || received > MaximumExecutableSize)
                    {
                        throw new InvalidDataException("updates.invalid-size");
                    }

                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    int percent = (int)(received * 100 / release.Size);
                    if (percent != reportedPercent)
                    {
                        progress?.Report(percent);
                        reportedPercent = percent;
                    }
                }

                string actualHash = Convert.ToHexString(hash.GetHashAndReset());
                if (received != release.Size || !actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("updates.checksum-mismatch");
                }
            }

            File.Move(partial, payload);
            return new DownloadedUpdate(payload, expectedHash.ToUpperInvariant(), release.Version);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    private async Task<string> ReadChecksumAsync(UpdateRelease release, CancellationToken cancellationToken)
    {
        if (release.ChecksumsUri is null)
        {
            throw new InvalidDataException("updates.missing-checksum");
        }

        using HttpResponseMessage response = await client.GetAsync(release.ChecksumsUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        RequireHttps(response);
        string text =
            Encoding.UTF8.GetString(await ReadLimitedAsync(response, cancellationToken).ConfigureAwait(false));
        string[] matches = text.Split('\n').Select(line => line.Trim().TrimStart('\uFEFF'))
            .Where(line => line.Length > 66 && line[64] == ' ' &&
                           line[64..].TrimStart(' ', '*').Equals(release.AssetName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 || ParseDigest("sha256:" + matches[0][..64]) is not { } hash)
        {
            throw new InvalidDataException("updates.missing-checksum");
        }

        return hash;
    }

    private Uri ValidateDownloadUri(string? value, string tag, string name)
    {
        var expected =
            new Uri(
                $"https://github.com/{owner}/{repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}");
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri != expected)
        {
            throw new InvalidDataException("updates.invalid-url");
        }

        return uri;
    }

    private static string? ParseDigest(string? digest)
    {
        return digest is { Length: 71 } && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
               digest[7..].All(char.IsAsciiHexDigit)
            ? digest[7..]
            : null;
    }

    private static void RequireHttps(HttpResponseMessage response)
    {
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("updates.insecure-download");
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var result = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (result.Length + count > 1024 * 1024)
            {
                throw new InvalidDataException("updates.response-too-large");
            }

            result.Write(buffer, 0, count);
        }

        return result.ToArray();
    }
}

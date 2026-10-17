using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.Core.Updates;
using NasMonitor.Infrastructure.Updates;

namespace NasMonitor.Connection.Tests.Updates;

[TestClass]
public sealed class GitHubUpdateTests
{
    private const string AssetName = "NasMonitor-win-x64.exe";

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
    public async Task FindsOnlyNewerVersionsAndSelectsTheExactPlatformAsset()
    {
        using var handler = new ReleaseHandler();
        using var http = new HttpClient(handler);
        var client = new GitHubReleaseClient(http, "owner", "repo", directory);

        UpdateCheckResult available = await client.CheckAsync(new Version(0, 1, 0), AssetName, CancellationToken.None);
        UpdateCheckResult equal = await client.CheckAsync(new Version(0, 2, 0, 0), AssetName, CancellationToken.None);
        UpdateCheckResult older = await client.CheckAsync(new Version(1, 0, 0), AssetName, CancellationToken.None);
        UpdateCheckResult wrongRid =
            await client.CheckAsync(new Version(0, 1, 0), "NasMonitor-osx-arm64", CancellationToken.None);

        Assert.AreEqual(UpdateCheckStatus.Available, available.Status);
        Assert.AreEqual(AssetName, available.Release!.AssetName);
        Assert.AreEqual(UpdateCheckStatus.UpToDate, equal.Status);
        Assert.AreEqual(UpdateCheckStatus.UpToDate, older.Status);
        Assert.AreEqual(UpdateCheckStatus.MissingAsset, wrongRid.Status);
        Assert.AreEqual(0, handler.DownloadRequests);
    }

    [TestMethod]
    public async Task MissingReleaseFeedIsNotReportedAsUpToDate()
    {
        using var handler = new ReleaseHandler { Status = HttpStatusCode.NotFound };
        using var http = new HttpClient(handler);
        var client = new GitHubReleaseClient(http, "owner", "repo", directory);

        UpdateCheckResult result = await client.CheckAsync(new Version(0, 1, 0), AssetName, CancellationToken.None);

        Assert.AreEqual(UpdateCheckStatus.NoReleases, result.Status);
        Assert.IsNull(result.Release);
    }

    [TestMethod]
    public async Task RateLimitIsPreservedForTheUiWithoutDownloadingAnything()
    {
        using var handler = new ReleaseHandler { Status = HttpStatusCode.Forbidden };
        using var http = new HttpClient(handler);
        var client = new GitHubReleaseClient(http, "owner", "repo", directory);

        HttpRequestException failure = await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
            client.CheckAsync(new Version(0, 1, 0), AssetName, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.Forbidden, failure.StatusCode);
        Assert.AreEqual(0, handler.DownloadRequests);
    }

    [TestMethod]
    public async Task PrereleasesAndUntrustedDownloadUrlsAreRejected()
    {
        using var handler = new ReleaseHandler { Prerelease = true };
        using var http = new HttpClient(handler);
        var client = new GitHubReleaseClient(http, "owner", "repo", directory);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            client.CheckAsync(new Version(0, 1, 0), AssetName, CancellationToken.None));
        handler.Prerelease = false;
        handler.DownloadUrl = "https://example.com/update.exe";

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            client.CheckAsync(new Version(0, 1, 0), AssetName, CancellationToken.None));
    }

    [TestMethod]
    public async Task DigestAndManifestBothSupportVerifiedDownloads()
    {
        foreach (bool useDigest in new[] { true, false })
        {
            using var handler = new ReleaseHandler { UseDigest = useDigest };
            using var http = new HttpClient(handler);
            var client = new GitHubReleaseClient(http, "owner", "repo", directory);
            UpdateRelease release = (await client.CheckAsync(new Version(0, 1, 0), AssetName, CancellationToken.None))
                .Release!;

            DownloadedUpdate downloaded = await client.DownloadAsync(release, null, CancellationToken.None);

            CollectionAssert.AreEqual(handler.Bytes, await File.ReadAllBytesAsync(downloaded.FilePath));
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(handler.Bytes)), downloaded.Sha256);
        }
    }

    [TestMethod]
    public async Task CorruptAndTruncatedExecutablesNeverBecomeInstallablePayloads()
    {
        using var handler = new ReleaseHandler();
        using var http = new HttpClient(handler);
        var client = new GitHubReleaseClient(http, "owner", "repo", directory);
        UpdateRelease release =
            (await client.CheckAsync(new Version(0, 1, 0), AssetName, CancellationToken.None)).Release!;
        handler.Bytes = [9, 8, 7, 6];
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            client.DownloadAsync(release, null, CancellationToken.None));
        handler.Bytes = [1];
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            client.DownloadAsync(release, null, CancellationToken.None));

        Assert.IsFalse(Directory.EnumerateFiles(directory, "payload.*", SearchOption.AllDirectories).Any());
    }

    [TestMethod]
    public async Task MissingOrAmbiguousChecksumsAreRejected()
    {
        using var handler = new ReleaseHandler { UseDigest = false };
        using var http = new HttpClient(handler);
        var client = new GitHubReleaseClient(http, "owner", "repo", directory);
        UpdateRelease release =
            (await client.CheckAsync(new Version(0, 1, 0), AssetName, CancellationToken.None)).Release!;
        handler.ManifestOverride = new string('A', 64) + "  other.exe";
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            client.DownloadAsync(release, null, CancellationToken.None));
        handler.ManifestOverride = string.Join('\n', Enumerable.Repeat(new string('A', 64) + "  " + AssetName, 2));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            client.DownloadAsync(release, null, CancellationToken.None));
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public async Task CancellationDeletesThePartialDownload()
    {
        using var handler = new ReleaseHandler { Bytes = new byte[200000] };
        using var http = new HttpClient(handler);
        var client = new GitHubReleaseClient(http, "owner", "repo", directory);
        UpdateRelease release =
            (await client.CheckAsync(new Version(0, 1, 0), AssetName, CancellationToken.None)).Release!;
        using var cancellation = new CancellationTokenSource();
        var progress = new CancellingProgress(cancellation);

        try
        {
            await client.DownloadAsync(release, progress, cancellation.Token);
            Assert.Fail("The cancelled download completed.");
        }
        catch (OperationCanceledException)
        {
            Assert.IsTrue(cancellation.IsCancellationRequested);
        }

        Assert.IsFalse(Directory.EnumerateFiles(directory, "payload.*", SearchOption.AllDirectories).Any());
    }

    private sealed class CancellingProgress(CancellationTokenSource cancellation) : IProgress<double>
    {
        public void Report(double value)
        {
            cancellation.Cancel();
        }
    }

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        internal byte[] Bytes
        {
            get;
            set;
        } = [1, 2, 3, 4];

        internal bool UseDigest
        {
            get;
            set;
        } = true;

        internal bool Prerelease
        {
            get;
            set;
        }

        internal HttpStatusCode Status
        {
            get;
            set;
        } = HttpStatusCode.OK;

        internal string DownloadUrl
        {
            get;
            set;
        } = "https://github.com/owner/repo/releases/download/v0.2.0/" + AssetName;

        internal string? ManifestOverride
        {
            get;
            set;
        }

        internal int DownloadRequests
        {
            get;
            private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string hash = Convert.ToHexString(SHA256.HashData(Bytes));
            HttpContent content;
            HttpStatusCode status = HttpStatusCode.OK;
            if (request.RequestUri!.Host == "api.github.com")
            {
                status = Status;
                content = new StringContent(JsonSerializer.Serialize(new
                    {
                        draft = false,
                        prerelease = Prerelease,
                        tag_name = "v0.2.0",
                        body = "Fixture notes",
                        assets = new[]
                        {
                            new
                            {
                                name = AssetName,
                                state = "uploaded",
                                size = Bytes.Length,
                                digest = UseDigest ? "sha256:" + hash : null,
                                browser_download_url = DownloadUrl
                            },
                            new
                            {
                                name = "SHA256SUMS.txt",
                                state = "uploaded",
                                size = 100,
                                digest = (string?)null,
                                browser_download_url =
                                    "https://github.com/owner/repo/releases/download/v0.2.0/SHA256SUMS.txt"
                            }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json");
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal))
            {
                content = new StringContent(ManifestOverride ?? hash + "  " + AssetName + "\n");
            }
            else
            {
                DownloadRequests++;
                content = new ByteArrayContent(Bytes);
            }

            return Task.FromResult(new HttpResponseMessage(status) { Content = content, RequestMessage = request });
        }
    }
}

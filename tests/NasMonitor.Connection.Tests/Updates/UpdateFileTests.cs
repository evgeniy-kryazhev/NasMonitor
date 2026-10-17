using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.Infrastructure.Updates;

namespace NasMonitor.Connection.Tests.Updates;

[TestClass]
public sealed class UpdateFileTests
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "NasMonitor.Tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        string resolved = Path.GetFullPath(directory);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NasMonitor.Tests")),
            Path.GetDirectoryName(resolved));
        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, true);
        }
    }

    [TestMethod]
    public async Task ReplacementPreservesBackupAndCanRollBack()
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "app.exe");
        string payload = Path.Combine(directory, "payload.bin");
        await File.WriteAllTextAsync(target, "original");
        await File.WriteAllTextAsync(payload, "new version");
        string originalHash = await UpdateFileTransaction.HashAsync(target, CancellationToken.None);
        string payloadHash = await UpdateFileTransaction.HashAsync(payload, CancellationToken.None);

        string backup =
            await UpdateFileTransaction.ReplaceAsync(payload,
                target,
                payloadHash,
                originalHash,
                CancellationToken.None);
        Assert.AreEqual("new version", await File.ReadAllTextAsync(target));
        Assert.AreEqual("original", await File.ReadAllTextAsync(backup));
        UpdateFileTransaction.Rollback(target, backup);
        Assert.AreEqual("original", await File.ReadAllTextAsync(target));
    }

    [TestMethod]
    public async Task ModifiedPayloadOrTargetNeverOverwritesTheInstalledFile()
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "app.exe");
        string payload = Path.Combine(directory, "payload.bin");
        await File.WriteAllTextAsync(target, "original");
        await File.WriteAllTextAsync(payload, "new version");
        string originalHash = await UpdateFileTransaction.HashAsync(target, CancellationToken.None);
        string payloadHash = await UpdateFileTransaction.HashAsync(payload, CancellationToken.None);
        await File.WriteAllTextAsync(payload, "modified");

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            UpdateFileTransaction.ReplaceAsync(payload, target, payloadHash, originalHash, CancellationToken.None));
        Assert.AreEqual("original", await File.ReadAllTextAsync(target));
        Assert.AreEqual(2, Directory.GetFiles(directory).Length);
    }
}

using System.Diagnostics;
using System.Text.Json;
using NasMonitor.Core.Updates;

namespace NasMonitor.Infrastructure.Updates;

/// <summary>Launches a private copy of the current portable executable as the update helper.</summary>
public sealed class PortableUpdateInstaller(string executablePath, string stagingRoot) : IAppUpdateInstaller
{
    /// <inheritdoc />
    public bool CanInstall => (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) &&
                              File.Exists(executablePath) &&
                              !Path.GetFileNameWithoutExtension(executablePath)
                                  .Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
                              !File.Exists(Path.Combine(Path.GetDirectoryName(executablePath)!,
                                  "NasMonitor.App.dll")) &&
                              !File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executablePath)!,
                                  "..",
                                  "_CodeSignature",
                                  "CodeResources")));

    /// <inheritdoc />
    public async Task PrepareAsync(DownloadedUpdate update, CancellationToken cancellationToken)
    {
        if (!CanInstall)
        {
            throw new InvalidOperationException("updates.not-portable");
        }

        string target = Path.GetFullPath(executablePath);
        string payload = Path.GetFullPath(update.FilePath);
        string directory = Path.GetDirectoryName(payload)!;
        string root = Path.GetFullPath(stagingRoot) + Path.DirectorySeparatorChar;
        StringComparison comparison =
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!directory.StartsWith(root, comparison) || Path.GetFileName(payload) != "payload.bin" ||
            !Guid.TryParseExact(Path.GetFileName(directory), "N", out _))
        {
            throw new InvalidDataException("updates.invalid-staging");
        }

        // Fail before asking the app to close if the installation folder is read-only.
        string probe = Path.Combine(Path.GetDirectoryName(target)!,
            ".nasmonitor-update-" + Guid.NewGuid().ToString("N"));
        using (var writable = new FileStream(probe,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   1,
                   FileOptions.DeleteOnClose))
        {
        }

        string originalHash = await UpdateFileTransaction.HashAsync(target, cancellationToken).ConfigureAwait(false);
        using var parent = Process.GetCurrentProcess();
        var job = new UpdateInstallJob(parent.Id,
            parent.StartTime.ToUniversalTime().Ticks,
            target,
            originalHash,
            update.Sha256,
            update.Version.ToString(3));
        string jobPath = Path.Combine(directory, "install.json");
        await File.WriteAllTextAsync(jobPath, JsonSerializer.Serialize(job), cancellationToken).ConfigureAwait(false);
        string helper = Path.Combine(directory, OperatingSystem.IsWindows() ? "updater.exe" : "updater");
        File.Copy(target, helper, false);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(helper, File.GetUnixFileMode(target));
        }

        var start = new ProcessStartInfo(helper)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(target)!
        };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add(jobPath);
        using Process process = Process.Start(start) ?? throw new IOException("updates.helper-start-failed");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (!File.Exists(Path.Combine(directory, "ready")))
            {
                if (process.HasExited)
                {
                    throw new IOException("updates.helper-start-failed");
                }

                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }

            // A cancellation before readiness must never install later when the user closes the app.
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "cancelled"), "cancelled", CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using NasMonitor.Core.Updates;

namespace NasMonitor.Infrastructure.Updates;

/// <summary>Runs before Avalonia initialization to replace a closed application's executable.</summary>
public static class UpdateHelper
{
    /// <summary>Validate a staged job, wait for its exact parent, install and relaunch.</summary>
    public static async Task<int> RunAsync(string jobPath)
    {
        UpdateInstallJob? job = null;
        string? backup = null;
        string? originalPlist = null;
        string? plistPath = null;
        bool parentExited = false;
        try
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(jobPath))!;
            string helper = Environment.ProcessPath ?? throw new InvalidDataException("updates.invalid-helper");
            if (!SamePath(Path.GetDirectoryName(helper)!, directory) || Path.GetFileName(jobPath) != "install.json")
            {
                return 1;
            }

            string json = await File.ReadAllTextAsync(jobPath).ConfigureAwait(false);
            job = JsonSerializer.Deserialize<UpdateInstallJob>(json) ??
                  throw new InvalidDataException("updates.invalid-job");
            string target = Path.GetFullPath(job.TargetPath);
            string payload = Path.Combine(directory, "payload.bin");
            if (job.ParentId <= 0 || job.ParentId == Environment.ProcessId ||
                !ReleaseVersion.TryParse(job.Version, out _) || !Path.IsPathFullyQualified(job.TargetPath) ||
                SamePath(target, helper) || target.StartsWith(directory + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                !string.Equals(await UpdateFileTransaction.HashAsync(payload, CancellationToken.None)
                        .ConfigureAwait(false),
                    job.PayloadSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("updates.invalid-job");
            }

            Process? parent = null;
            try
            {
                parent = Process.GetProcessById(job.ParentId);
                if (parent.StartTime.ToUniversalTime().Ticks != job.ParentStartTicks ||
                    !SamePath(parent.MainModule?.FileName ?? "", target))
                {
                    throw new InvalidDataException("updates.parent-mismatch");
                }
            }
            catch (ArgumentException)
            {
                // A parent that already exited needs no wait; never attach to a reused PID.
            }

            await File.WriteAllTextAsync(Path.Combine(directory, "ready"), "ready").ConfigureAwait(false);
            using (parent)
            {
                if (parent is not null)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    while (!parent.HasExited)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        if (File.Exists(Path.Combine(directory, "cancelled")))
                        {
                            return 1;
                        }

                        await Task.Delay(100, timeout.Token).ConfigureAwait(false);
                    }
                }
            }

            parentExited = true;
            if (File.Exists(Path.Combine(directory, "cancelled")))
            {
                return 1;
            }

            backup = await UpdateFileTransaction.ReplaceAsync(payload,
                target,
                job.PayloadSha256,
                job.OriginalSha256,
                CancellationToken.None).ConfigureAwait(false);
            if (OperatingSystem.IsMacOS() && Path.GetFileName(Path.GetDirectoryName(target)) == "MacOS")
            {
                plistPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target)!, "..", "Info.plist"));
                if (File.Exists(plistPath))
                {
                    originalPlist = await File.ReadAllTextAsync(plistPath).ConfigureAwait(false);
                    var plist = XDocument.Parse(originalPlist, LoadOptions.PreserveWhitespace);
                    foreach (XElement key in plist.Descendants("key").Where(key =>
                                 key.Value is "CFBundleVersion" or "CFBundleShortVersionString"))
                    {
                        key.ElementsAfterSelf().FirstOrDefault()?.SetValue(job.Version);
                    }

                    await File.WriteAllTextAsync(plistPath, plist.ToString()).ConfigureAwait(false);
                }
            }

            WriteResult(directory, "UpdateInstalled");
            Launch(target);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or JsonException or Win32Exception
                                              or ArgumentException or
                                              OperationCanceledException or XmlException or NotSupportedException)
        {
            if (job is not null && parentExited)
            {
                try
                {
                    if (backup is not null)
                    {
                        UpdateFileTransaction.Rollback(job.TargetPath, backup);
                    }

                    if (originalPlist is not null && plistPath is not null)
                    {
                        await File.WriteAllTextAsync(plistPath, originalPlist).ConfigureAwait(false);
                    }

                    WriteResult(Path.GetDirectoryName(jobPath)!, "UpdateInstallFailed");
                    Launch(job.TargetPath);
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or Win32Exception)
                {
                    // The previous executable backup remains available for manual recovery.
                }
            }

            return 1;
        }
    }

    private static bool SamePath(string first, string second)
    {
        return string.Equals(Path.GetFullPath(first),
            Path.GetFullPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void WriteResult(string directory, string code)
    {
        string root = Path.GetDirectoryName(directory)!;
        try
        {
            File.WriteAllText(Path.Combine(root, "last-result.txt"), code);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Status persistence must not prevent launching the installed or restored executable.
        }
    }

    private static void Launch(string target)
    {
        using var process = Process.Start(new ProcessStartInfo(target)
        {
            UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)!
        });
        if (process is null)
        {
            throw new IOException("updates.restart-failed");
        }
    }
}

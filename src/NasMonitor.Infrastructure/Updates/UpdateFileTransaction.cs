using System.Security.Cryptography;

namespace NasMonitor.Infrastructure.Updates;

/// <summary>Verifies bytes before replacing an executable, retaining a sibling rollback file.</summary>
public static class UpdateFileTransaction
{
    /// <summary>Read and hash a file without loading an entire executable into memory.</summary>
    public static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            65536,
            FileOptions.Asynchronous);
        byte[] hash = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    /// <summary>Verify both files, stage beside the target, then swap with rollback on failure.</summary>
    public static async Task<string> ReplaceAsync(string payload,
        string target,
        string payloadHash,
        string originalHash,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(await HashAsync(payload, cancellationToken).ConfigureAwait(false),
                payloadHash,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(await HashAsync(target, cancellationToken).ConfigureAwait(false),
                originalHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("updates.checksum-mismatch");
        }

        string suffix = Guid.NewGuid().ToString("N");
        string pending = target + ".update-" + suffix;
        string backup = target + ".backup-" + suffix;
        try
        {
            File.Copy(payload, pending, false);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(pending, File.GetUnixFileMode(target));
            }

            if (!string.Equals(await HashAsync(pending, cancellationToken).ConfigureAwait(false),
                    payloadHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("updates.checksum-mismatch");
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Both renames stay on the target filesystem; no cancellation between them.
            File.Move(target, backup);
            try
            {
                File.Move(pending, target);
            }
            catch
            {
                File.Move(backup, target);
                throw;
            }

            return backup;
        }
        finally
        {
            if (File.Exists(pending))
            {
                File.Delete(pending);
            }
        }
    }

    /// <summary>Restore a replacement that failed to launch; preserve its bytes for diagnosis.</summary>
    public static void Rollback(string target, string backup)
    {
        string failed = target + ".failed-" + Guid.NewGuid().ToString("N");
        File.Move(target, failed);
        try
        {
            File.Move(backup, target);
        }
        catch
        {
            File.Move(failed, target);
            throw;
        }
    }
}

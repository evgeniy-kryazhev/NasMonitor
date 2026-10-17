using System.Globalization;

namespace NasMonitor.Core.Updates;

/// <summary>Parses stable release tags without accepting prerelease labels or ambiguous versions.</summary>
public static class ReleaseVersion
{
    /// <summary>Accept an optional v prefix followed by three nonnegative decimal components.</summary>
    public static bool TryParse(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        string value = (tag ?? "").Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        string[] parts = value.Split('.');
        if (parts.Length != 3 || parts.Any(part => part.Length == 0 ||
                                                   (part.Length > 1 && part[0] == '0') ||
                                                   !int.TryParse(part,
                                                       NumberStyles.None,
                                                       CultureInfo.InvariantCulture,
                                                       out _)))
        {
            return false;
        }

        return Version.TryParse(value, out version!);
    }
}

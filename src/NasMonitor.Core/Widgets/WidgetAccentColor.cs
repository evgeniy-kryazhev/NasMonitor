namespace NasMonitor.Core.Widgets;

/// <summary>Validates the opaque RGB format shared by persisted settings and the editor.</summary>
public static class WidgetAccentColor
{
    /// <summary>Accept six hexadecimal digits and return the canonical RGB representation.</summary>
    public static bool TryNormalize(string? value, out string normalized)
    {
        string candidate = value?.Trim() ?? "";
        if (candidate.Length != 7 || candidate[0] != '#' || !candidate.Skip(1).All(char.IsAsciiHexDigit))
        {
            normalized = "";
            return false;
        }

        normalized = candidate.ToUpperInvariant();
        return true;
    }
}

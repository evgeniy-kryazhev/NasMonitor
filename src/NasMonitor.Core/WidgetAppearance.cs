namespace NasMonitor.Core;

/// <summary>Widget-only theme selection; FollowApplication tracks the shell theme.</summary>
public enum WidgetTheme
{
    FollowApplication,
    Light,
    Dark
}

/// <summary>Non-secret widget presentation settings, independent of NAS connection profiles.</summary>
public sealed record WidgetAppearance(string Title = "NAS", string AccentColor = "#35B89A",
    double Opacity = 1, double CornerRadius = 14, bool ShowCpu = true, bool ShowRam = true,
    bool ShowLastUpdate = true, WidgetTheme Theme = WidgetTheme.FollowApplication,
    bool ShowStorage = true, bool ShowNetwork = true, bool ShowTemperature = true, bool ShowUptime = true)
{
    /// <summary>Normalize persisted values and reject invalid colors without any UI dependency.</summary>
    /// <returns>Settings safe to use for both UI binding and persistence.</returns>
    public WidgetAppearance Normalize()
    {
        string title = string.IsNullOrWhiteSpace(Title) ? "NAS" : Title.Trim();
        if (title.Length > 60)
        {
            title = title[..60];
        }
        string color = AccentColor?.Trim() ?? "";
        bool validColor = color.Length == 7 && color[0] == '#' && color.Skip(1).All(char.IsAsciiHexDigit);
        return this with
        {
            Title = title,
            AccentColor = validColor ? color.ToUpperInvariant() : "#35B89A",
            Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.35, 1) : 1,
            CornerRadius = double.IsFinite(CornerRadius) ? Math.Clamp(CornerRadius, 0, 32) : 14,
            Theme = Enum.IsDefined(Theme) ? Theme : WidgetTheme.FollowApplication
        };
    }
}

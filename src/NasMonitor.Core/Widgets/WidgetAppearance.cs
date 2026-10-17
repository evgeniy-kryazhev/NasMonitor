namespace NasMonitor.Core.Widgets;

/// <summary>Non-secret widget presentation settings, independent of NAS connection profiles.</summary>
public sealed record WidgetAppearance(
    string Title = "NAS",
    string AccentColor = "#35B89A",
    double Opacity = 1,
    double CornerRadius = 14,
    bool ShowCpu = true,
    bool ShowRam = true,
    bool ShowLastUpdate = true,
    WidgetTheme Theme = WidgetTheme.FollowApplication,
    bool ShowStorage = true,
    bool ShowNetwork = true,
    bool ShowTemperature = true,
    bool ShowUptime = true)
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

        bool validColor = WidgetAccentColor.TryNormalize(AccentColor, out string color);
        return this with
        {
            Title = title,
            AccentColor = validColor ? color : "#35B89A",
            Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.35, 1) : 1,
            CornerRadius = double.IsFinite(CornerRadius) ? Math.Clamp(CornerRadius, 0, 32) : 14,
            Theme = Enum.IsDefined(Theme) ? Theme : WidgetTheme.FollowApplication
        };
    }
}

using System.Globalization;
using System.Resources;

namespace NasMonitor.App.Localization;

internal static class UiText
{
    private static readonly ResourceManager resources = new("NasMonitor.App.Localization.Strings",
        typeof(UiText).Assembly);

    public static string Get(string key)
    {
        return resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }
}

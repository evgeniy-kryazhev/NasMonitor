using System.Globalization;
using System.Resources;
using Avalonia.Markup.Xaml;

namespace NasMonitor.App;

/// <summary>Resolves UI text through standard .NET satellite resources; RU is the fallback.</summary>
public sealed class TextExtension(string key) : MarkupExtension
{
    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Get(key);
}

internal static class UiText
{
    private static readonly ResourceManager Resources = new("NasMonitor.App.Strings", typeof(UiText).Assembly);

    public static string Get(string key)
    {
        return Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }
}

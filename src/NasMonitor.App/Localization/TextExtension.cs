using Avalonia.Markup.Xaml;

namespace NasMonitor.App.Localization;

/// <summary>Resolves UI text through standard .NET satellite resources; RU is the fallback.</summary>
public sealed class TextExtension(string key) : MarkupExtension
{
    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return UiText.Get(key);
    }
}

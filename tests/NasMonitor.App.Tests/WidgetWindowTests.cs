using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(NasMonitor.App.Tests.TestApplication))]

namespace NasMonitor.App.Tests;

public static class TestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class WidgetWindowTests
{
    [AvaloniaFact]
    public void SharedWidgetShowsDemoAndNeverClaimsRealNasConnectivity()
    {
        var window = new WidgetWindow();
        try
        {
            window.Show();
            var labels = window.GetLogicalDescendants().OfType<TextBlock>().Select(x => x.Text).ToArray();
            Assert.Contains("Not connected to a real NAS.", labels);
            Assert.Contains(labels, x => x is not null && x.Contains("DEMO"));
            Assert.False(window.Topmost);
            Assert.Equal(2, window.GetLogicalDescendants().OfType<Button>().Count());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AttachWithoutNativeHandleRemainsRecoverableAndCanClose()
    {
        var window = new WidgetWindow();
        try
        {
            window.Show();
            window.RequestAttachment();
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(),
                x => x.Text is not null && x.Text.Contains("unavailable"));
            var restore = window.GetLogicalDescendants().OfType<Button>()
                .Single(x => Equals(x.Content, "Restore normal window"));
            restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(),
                x => x.Text is not null && x.Text.Contains("Normal window"));
        }
        finally { window.Close(); }
        Assert.False(window.IsVisible);
    }
}

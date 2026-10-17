using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using NasMonitor.App.ViewModels;
using NasMonitor.Core.Profiles;
using NasMonitor.Core.Widgets;

namespace NasMonitor.App.Views.Windows;

public partial class ManagerWindow
{
    internal async Task CaptureSmokePagesAsync()
    {
        Directory.CreateDirectory("artifacts/ui");
        widgetSettings.VerifyLivePreview();
        await widgetSettings.VerifyColorPickerFlyoutAsync();
        await widgetSettings.VerifySettingsTabsAsync();
        await widgetSettings.VerifyFlexibleHeightAsync();
        if (widgetController is not null)
        {
            await widgetController.VerifyContentMinimumHeightAsync();
        }

        await VerifySidebarNasAsync();
        foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            ThemeSelector.SelectedIndex = theme == ThemeVariant.Light ? 1 : 2;
            foreach (int page in new[] { 0, 1 })
            {
                Navigation.SelectedIndex = page;
                // Allow theme, bindings and layout to settle before rendering our own visual tree.
                await Task.Delay(200);
                foreach (string key in new[] { "SemiColorBackground0", "SemiColorNavBackground", "SemiColorBorder" })
                {
                    if (!this.TryFindResource(key, out object? resource) || resource is not IBrush)
                    {
                        throw new InvalidOperationException("semi.resource-unavailable");
                    }
                }

                string pageName = page == 0 ? "widget" : "connections";
                SaveSmokeImage($"artifacts/ui/{pageName}-{theme.Key}.png");
            }
        }

        if (widgetSettings.DataContext is WidgetViewModel viewModel)
        {
            WidgetAppearance original = viewModel.CurrentAppearance;
            var sample = new WidgetAppearance("Home NAS",
                "#799CE8",
                0.8,
                24,
                ShowRam: false,
                Theme: WidgetTheme.Dark);
            try
            {
                ThemeSelector.SelectedIndex = 1;
                Navigation.SelectedIndex = 0;
                widgetSettings.DisplayAppearance(sample);
                await Task.Delay(200);
                SaveSmokeImage("artifacts/ui/widget-custom.png");
            }
            finally
            {
                widgetSettings.DisplayAppearance(original);
            }
        }

        double originalWidth = Width;
        double originalHeight = Height;
        try
        {
            Width = 1500;
            Navigation.SelectedIndex = 0;
            await Task.Delay(200);
            widgetSettings.VerifyPageLayout();
            SaveSmokeImage("artifacts/ui/widget-wide.png");
            widgetSettings.SelectSettingsTab(1);
            await Task.Delay(200);
            widgetSettings.VerifyPageLayout();
            SaveSmokeImage("artifacts/ui/widget-placement.png");
            widgetSettings.SelectSettingsTab(0);
            Navigation.SelectedIndex = 1;
            await Task.Delay(200);
            connections.VerifyPageLayout();
            SaveSmokeImage("artifacts/ui/connections-wide.png");
            Width = 1100;
            Height = 650;
            Navigation.SelectedIndex = 0;
            await Task.Delay(200);
            widgetSettings.VerifyPageLayout();
            SaveSmokeImage("artifacts/ui/widget-short.png");
            widgetSettings.SelectSettingsTab(1);
            await Task.Delay(200);
            widgetSettings.VerifyPageLayout();
            widgetSettings.SelectSettingsTab(0);
        }
        finally
        {
            Width = originalWidth;
            Height = originalHeight;
        }
    }

    private void SaveSmokeImage(string path)
    {
        var size = new PixelSize((int)Math.Ceiling(ClientSize.Width * RenderScaling),
            (int)Math.Ceiling(ClientSize.Height * RenderScaling));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * RenderScaling, 96 * RenderScaling));
        bitmap.Render(this);
        bitmap.Save(path);
    }

    private async Task VerifySidebarNasAsync()
    {
        if (connectionsViewModel is not { } viewModel)
        {
            throw new InvalidOperationException("sidebar.viewmodel-unavailable");
        }

        NasProfile[] originalProfiles = viewModel.Profiles.ToArray();
        NasProfile? originalSelection = viewModel.SelectedProfile;
        var first = new NasProfile(Guid.NewGuid(), "Home NAS · TEST", new Uri("https://home.example:5001"), "fixture");
        var second = new NasProfile(Guid.NewGuid(),
            "Backup NAS · TEST",
            new Uri("https://backup.example:5001"),
            "fixture");
        try
        {
            viewModel.Profiles.Clear();
            viewModel.Profiles.Add(first);
            viewModel.Profiles.Add(second);
            Navigation.SelectedIndex = 0;
            SidebarNas.SelectedItem = second;
            await Task.Delay(100);
            if (Navigation.SelectedIndex != 1 || viewModel.SelectedProfile?.Id != second.Id ||
                SidebarNas.ItemCount != 2)
            {
                throw new InvalidOperationException("sidebar.selection-failed");
            }

            Navigation.SelectedIndex = 0;
            OpenSelectedNas();
            if (Navigation.SelectedIndex != 1)
            {
                throw new InvalidOperationException("sidebar.reselection-failed");
            }

            viewModel.Profiles[0] = first with { Name = "Home NAS renamed · TEST" };
            await Task.Delay(100);
            SaveSmokeImage("artifacts/ui/sidebar-nas.png");
            viewModel.Profiles.RemoveAt(0);
            if (SidebarNas.ItemCount != 1)
            {
                throw new InvalidOperationException("sidebar.removal-failed");
            }
        }
        finally
        {
            viewModel.Profiles.Clear();
            foreach (NasProfile profile in originalProfiles)
            {
                viewModel.Profiles.Add(profile);
            }

            viewModel.SelectedProfile = originalSelection;
            Navigation.SelectedIndex = 0;
        }
    }
}

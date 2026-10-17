using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using NasMonitor.App.Models;
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
        if (workspace is not null)
        {
            await workspace.VerifyMultipleWidgetsAsync(VerifyWidgetEditingAsync);
        }

        foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            ThemeSelector.SelectedIndex = theme == ThemeVariant.Light ? 1 : 2;
            foreach (int page in new[] { 0, 1, 2 })
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

                string pageName = page switch { 0 => "widget", 1 => "connections", _ => "updates" };
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

    private Task VerifyWidgetEditingAsync()
    {
        if (workspace is null || workspace.Widgets.Count < 2)
        {
            throw new InvalidOperationException("widgets.editor-unavailable");
        }

        WidgetInstance first = workspace.Widgets[0];
        WidgetInstance second = workspace.Widgets[1];
        Guid? firstNas = first.ViewModel.SelectedNas?.Id;
        Guid? secondNas = second.ViewModel.SelectedNas?.Id;
        workspace.SelectedWidget = first;
        widgetSettings.DisplayAppearance(first.ViewModel.CurrentAppearance with { Title = "Primary draft" });
        workspace.SelectedWidget = second;
        widgetSettings.VerifySourceForSmoke(secondNas);
        widgetSettings.DisplayAppearance(second.ViewModel.CurrentAppearance with { Title = "Backup draft" });
        workspace.SelectedWidget = first;
        widgetSettings.VerifySourceForSmoke(firstNas);
        if (!ReferenceEquals(widgetSettings.DataContext, first.ViewModel) ||
            widgetSettings.CaptureAppearanceDraft().Title != "Primary draft")
        {
            throw new InvalidOperationException("widgets.first-draft-lost");
        }

        workspace.SelectedWidget = second;
        if (widgetSettings.CaptureAppearanceDraft().Title != "Backup draft")
        {
            throw new InvalidOperationException("widgets.second-draft-lost");
        }

        NasProfile selected = second.ViewModel.SelectedNas!;
        widgetSettings.SelectNasForSmoke(first.ViewModel.SelectedNas!);
        if (second.ViewModel.SelectedNas?.Id != firstNas || first.ViewModel.SelectedNas?.Id != firstNas)
        {
            throw new InvalidOperationException("widgets.source-control-failed");
        }

        widgetSettings.SelectNasForSmoke(selected);
        widgetSettings.DisplayAppearance(second.ViewModel.CurrentAppearance);
        workspace.SelectedWidget = first;
        widgetSettings.DisplayAppearance(first.ViewModel.CurrentAppearance);
        return Task.CompletedTask;
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

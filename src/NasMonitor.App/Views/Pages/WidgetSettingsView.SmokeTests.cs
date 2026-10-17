using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using NasMonitor.Core.Profiles;
using NasMonitor.Core.Widgets;

namespace NasMonitor.App.Views.Pages;

public partial class WidgetSettingsView
{
    internal void VerifySourceForSmoke(Guid? expectedNasId)
    {
        if ((SourceSelector.SelectedItem as NasProfile)?.Id != expectedNasId)
        {
            throw new InvalidOperationException("widgets.source-display-failed");
        }
    }

    internal void SelectNasForSmoke(NasProfile profile)
    {
        SourceSelector.SelectedItem = profile;
    }

    internal async Task VerifyColorPickerFlyoutAsync()
    {
        Button? pickerButton = AppearanceColorPicker.GetVisualDescendants()
            .OfType<Button>().FirstOrDefault(button => button.Flyout is Flyout);
        if (pickerButton?.Flyout is not Flyout flyout)
        {
            throw new InvalidOperationException("picker.flyout-unavailable");
        }

        flyout.ShowAt(pickerButton);
        try
        {
            await Task.Delay(200);
            if (!flyout.IsOpen || flyout.Content is not Control content || content.Bounds.Width <= 0)
            {
                throw new InvalidOperationException("picker.flyout-layout-failed");
            }

            using var bitmap = new RenderTargetBitmap(
                new PixelSize((int)Math.Ceiling(content.Bounds.Width), (int)Math.Ceiling(content.Bounds.Height)));
            bitmap.Render(content);
            bitmap.Save("artifacts/ui/accent-color-picker.png");
        }
        finally
        {
            flyout.Hide();
        }
    }

    internal void VerifyLivePreview()
    {
        WidgetAppearance original = source?.CurrentAppearance ?? new WidgetAppearance();
        try
        {
            AppearanceName.Text = "Live preview";
            AppearanceAccent.Text = "#799CE8";
            if (AppearanceColorPicker.Color != Color.Parse("#799CE8"))
            {
                throw new InvalidOperationException("preview.hex-to-picker-failed");
            }

            AppearanceColorPicker.Color = Color.Parse("#E89979");
            if (AppearanceAccent.Text != "#E89979" || preview.CurrentAppearance.AccentColor != "#E89979")
            {
                throw new InvalidOperationException("preview.picker-to-hex-failed");
            }

            AppearanceAccent.Text = "#799CE8";
            AppearanceOpacity.Text = "65";
            AppearanceCorners.Text = "24";
            AppearanceShowCpu.IsChecked = false;
            AppearanceShowRam.IsChecked = false;
            AppearanceShowUpdate.IsChecked = false;
            AppearanceTheme.SelectedIndex = (int)WidgetTheme.Dark;
            if (preview.WidgetTitle != "Live preview" || Math.Abs(preview.SurfaceOpacity - 0.65) > 0.0001 ||
                Math.Abs(preview.SurfaceCornerRadius.TopLeft - 24) > 0.0001 || preview.ShowCpu || preview.ShowRam ||
                preview.ShowLastUpdate || preview.CurrentAppearance.Theme != WidgetTheme.Dark ||
                source?.CurrentAppearance != original)
            {
                throw new InvalidOperationException("preview.live-update-failed");
            }

            AppearanceAccent.Text = "#7";
            if (preview.CurrentAppearance.AccentColor != "#799CE8")
            {
                throw new InvalidOperationException("preview.partial-color-failed");
            }
        }
        finally
        {
            DisplayAppearance(original);
        }
    }

    internal void VerifyPageLayout()
    {
        // Avalonia rounds nested layout boundaries to physical pixels at fractional DPI.
        double tolerance = 1.01 / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        ScrollViewer activeScroll = SettingsTabs.SelectedIndex == 1 ? PlacementScroll : AppearanceScroll;
        StackPanel activeContent = SettingsTabs.SelectedIndex == 1 ? PlacementContent : AppearanceContent;
        Point? scrollBottom =
            activeScroll.TranslatePoint(new Point(activeScroll.Bounds.Width, activeScroll.Bounds.Height), this);
        Point? contentRight = activeContent.TranslatePoint(new Point(activeContent.Bounds.Width, 0), this);
        Point? headerRight = HeaderContent.TranslatePoint(new Point(HeaderContent.Bounds.Width, 0), this);
        File.WriteAllText("artifacts/ui/widget-layout.json",
            JsonSerializer.Serialize(new
            {
                Page = Bounds.ToString(),
                Content = PageContent.Bounds.ToString(),
                Tabs = SettingsTabs.Bounds.ToString(),
                ActiveTab = SettingsTabs.SelectedIndex,
                Scroll = activeScroll.Bounds.ToString(),
                ScrollBottom = scrollBottom?.ToString(),
                ContentRight = contentRight?.X,
                HeaderRight = headerRight?.X
            }));
        if (Math.Abs(PageContent.Bounds.X - 28) > tolerance ||
            Math.Abs(PageContent.Bounds.Right - Bounds.Width) > tolerance ||
            Math.Abs(PageContent.Bounds.Bottom - Bounds.Height) > tolerance ||
            Math.Abs(SettingsTabs.Bounds.Bottom - PageContent.Bounds.Height) > tolerance)
        {
            throw new InvalidOperationException("widget.page-layout-failed");
        }

        if (scrollBottom is null || Math.Abs(scrollBottom.Value.Y - Bounds.Height) > tolerance ||
            Math.Abs(scrollBottom.Value.X - Bounds.Width) > tolerance)
        {
            throw new InvalidOperationException("widget.scroll-edges-failed");
        }

        if (contentRight is null || headerRight is null ||
            Math.Abs(contentRight.Value.X - (Bounds.Width - 28)) > tolerance ||
            Math.Abs(contentRight.Value.X - headerRight.Value.X) > tolerance)
        {
            throw new InvalidOperationException("widget.content-insets-failed");
        }
    }

    internal void SelectSettingsTab(int index)
    {
        SettingsTabs.SelectedIndex = index;
    }

    internal async Task VerifySettingsTabsAsync()
    {
        SettingsTabs.SelectedIndex = 1;
        await Task.Delay(200);
        if (WidgetHeight.GetVisualRoot() is null || !WidgetHeight.IsEnabled ||
            AppearanceName.GetVisualRoot() is not null)
        {
            throw new InvalidOperationException("widget.placement-tab-failed");
        }

        SettingsTabs.SelectedIndex = 0;
        await Task.Delay(200);
        if (AppearanceName.GetVisualRoot() is null || WidgetHeight.GetVisualRoot() is not null)
        {
            throw new InvalidOperationException("widget.appearance-tab-failed");
        }
    }

    internal async Task VerifyFlexibleHeightAsync()
    {
        WidgetAppearance original = source?.CurrentAppearance ?? new WidgetAppearance();
        try
        {
            DisplayAppearance(original with
            {
                ShowCpu = true,
                ShowRam = true,
                ShowStorage = true,
                ShowNetwork = true,
                ShowTemperature = true,
                ShowUptime = true
            });
            await Task.Delay(200);
            double expandedHeight = AppearancePreview.Bounds.Height;
            DisplayAppearance(original with
            {
                ShowCpu = false,
                ShowRam = false,
                ShowStorage = false,
                ShowNetwork = false,
                ShowTemperature = false,
                ShowUptime = false
            });
            await Task.Delay(200);
            double compactHeight = AppearancePreview.Bounds.Height;
            if (expandedHeight < compactHeight + 100 || compactHeight > 150)
            {
                throw new InvalidOperationException("preview.flexible-height-failed");
            }
        }
        finally
        {
            DisplayAppearance(original);
        }
    }
}

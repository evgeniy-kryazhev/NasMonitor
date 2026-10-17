using Avalonia.Controls;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.ComponentModel;
using System.Globalization;
using NasMonitor.Core;

namespace NasMonitor.App;

/// <summary>Widget placement controls, separate from connection editing and navigation.</summary>
public partial class WidgetSettingsView : UserControl, IDisposable
{
    private WidgetController? controller;
    private bool widgetLocked = true;
    private readonly SpikeViewModel preview = new();
    private SpikeViewModel? source;
    private bool populatingAppearance;

    /// <summary>Create widget settings controls.</summary>
    public WidgetSettingsView()
    {
        InitializeComponent();
        AppearancePreview.DataContext = preview;
        AppearanceName.TextChanged += (_, _) => UpdatePreview();
        AppearanceAccent.TextChanged += (_, _) => UpdatePreview();
        AppearanceOpacity.ValueChanged += (_, _) => UpdatePreview();
        AppearanceCorners.ValueChanged += (_, _) => UpdatePreview();
        AppearanceOpacity.PropertyChanged += (_, args) =>
        {
            if (args.Property == NumericUpDown.TextProperty)
            {
                UpdatePreview();
            }
        };
        AppearanceCorners.PropertyChanged += (_, args) =>
        {
            if (args.Property == NumericUpDown.TextProperty)
            {
                UpdatePreview();
            }
        };
        AppearanceTheme.SelectionChanged += (_, _) => UpdatePreview();
        AppearanceShowCpu.IsCheckedChanged += (_, _) => UpdatePreview();
        AppearanceShowRam.IsCheckedChanged += (_, _) => UpdatePreview();
        AppearanceShowUpdate.IsCheckedChanged += (_, _) => UpdatePreview();
        AppearanceShowStorage.IsCheckedChanged += (_, _) => UpdatePreview();
        AppearanceShowNetwork.IsCheckedChanged += (_, _) => UpdatePreview();
        AppearanceShowTemperature.IsCheckedChanged += (_, _) => UpdatePreview();
        AppearanceShowUptime.IsCheckedChanged += (_, _) => UpdatePreview();
    }

    internal void Initialize(WidgetController widgetController, SpikeViewModel viewModel)
    {
        controller = widgetController;
        source = viewModel;
        source.PropertyChanged += SourceChanged;
        preview.RefreshRequested += RefreshSource;
        DataContext = viewModel;
        CopyMetrics();
    }

    internal void DisplayLayout(WidgetLayout layout)
    {
        PositionX.Value = layout.X;
        PositionY.Value = layout.Y;
        WidgetWidth.Value = (decimal)layout.Width;
        WidgetHeight.Value = (decimal)layout.Height;
        Locked.IsChecked = layout.Locked;
        widgetLocked = layout.Locked;
        DragModeButton.Content = UiText.Get(layout.Locked ? "EnableDragMode" : "FinishDragMode");
    }

    internal void DisplayAppearance(WidgetAppearance appearance)
    {
        populatingAppearance = true;
        AppearanceName.Text = appearance.Title;
        AppearanceAccent.Text = appearance.AccentColor;
        AppearanceOpacity.Value = (decimal)(appearance.Opacity * 100);
        AppearanceCorners.Value = (decimal)appearance.CornerRadius;
        AppearanceShowCpu.IsChecked = appearance.ShowCpu;
        AppearanceShowRam.IsChecked = appearance.ShowRam;
        AppearanceShowUpdate.IsChecked = appearance.ShowLastUpdate;
        AppearanceShowStorage.IsChecked = appearance.ShowStorage;
        AppearanceShowNetwork.IsChecked = appearance.ShowNetwork;
        AppearanceShowTemperature.IsChecked = appearance.ShowTemperature;
        AppearanceShowUptime.IsChecked = appearance.ShowUptime;
        AppearanceTheme.SelectedIndex = (int)appearance.Theme;
        AppearanceError.Text = "";
        populatingAppearance = false;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (populatingAppearance)
        {
            return;
        }
        string color = AppearanceAccent.Text?.Trim() ?? "";
        bool validColor = color.Length == 7 && color[0] == '#' && color.Skip(1).All(char.IsAsciiHexDigit);
        // A partially typed HEX must not reset the rest of the draft or the last valid color.
        WidgetAppearance draft = ReadAppearance(validColor ? color : preview.CurrentAppearance.AccentColor);
        preview.ApplyAppearance(draft);
        AppearanceError.Text = validColor ? "" : UiText.Get("AppearanceInvalidColor");
    }

    private WidgetAppearance ReadAppearance(string color)
    {
        return new WidgetAppearance(AppearanceName.Text ?? "", color,
            ReadNumber(AppearanceOpacity, 100) / 100,
            ReadNumber(AppearanceCorners, 14), AppearanceShowCpu.IsChecked == true,
            AppearanceShowRam.IsChecked == true, AppearanceShowUpdate.IsChecked == true,
            (WidgetTheme)Math.Max(0, AppearanceTheme.SelectedIndex),
            AppearanceShowStorage.IsChecked == true, AppearanceShowNetwork.IsChecked == true,
            AppearanceShowTemperature.IsChecked == true, AppearanceShowUptime.IsChecked == true);
    }

    private static double ReadNumber(NumericUpDown control, decimal fallback)
    {
        if (decimal.TryParse(control.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal parsed))
        {
            return (double)parsed;
        }
        return (double)(control.Value ?? fallback);
    }

    internal void VerifyLivePreview()
    {
        WidgetAppearance original = source?.CurrentAppearance ?? new WidgetAppearance();
        try
        {
            AppearanceName.Text = "Live preview";
            AppearanceAccent.Text = "#799CE8";
            AppearanceOpacity.Text = "65";
            AppearanceCorners.Text = "24";
            AppearanceShowCpu.IsChecked = false;
            AppearanceShowRam.IsChecked = false;
            AppearanceShowUpdate.IsChecked = false;
            AppearanceTheme.SelectedIndex = (int)WidgetTheme.Dark;
            if (preview.WidgetTitle != "Live preview" || preview.SurfaceOpacity != 0.65 ||
                preview.SurfaceCornerRadius.TopLeft != 24 || preview.ShowCpu || preview.ShowRam ||
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
        Point? scrollBottom = activeScroll.TranslatePoint(new Point(activeScroll.Bounds.Width, activeScroll.Bounds.Height), this);
        Point? contentRight = activeContent.TranslatePoint(new Point(activeContent.Bounds.Width, 0), this);
        Point? headerRight = HeaderContent.TranslatePoint(new Point(HeaderContent.Bounds.Width, 0), this);
        File.WriteAllText("artifacts/ui/widget-layout.json", System.Text.Json.JsonSerializer.Serialize(new
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
        if (WidgetHeight.GetVisualRoot() is null || !WidgetHeight.IsEnabled || AppearanceName.GetVisualRoot() is not null)
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
                ShowCpu = true, ShowRam = true, ShowStorage = true, ShowNetwork = true,
                ShowTemperature = true, ShowUptime = true
            });
            await Task.Delay(200);
            double expandedHeight = AppearancePreview.Bounds.Height;
            DisplayAppearance(original with
            {
                ShowCpu = false, ShowRam = false, ShowStorage = false, ShowNetwork = false,
                ShowTemperature = false, ShowUptime = false
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

    private void SourceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SpikeViewModel.MetricsRevision))
        {
            CopyMetrics();
        }
    }

    private void CopyMetrics()
    {
        if (source is null)
        {
            return;
        }
        preview.SelectedNas = source.SelectedNas;
        if (source.LastDemoSnapshot is { } demo)
        {
            preview.Update(demo);
        }
        else
        {
            preview.ApplyMonitoringState(source.DisplayState, DateTimeOffset.UtcNow);
        }
    }

    private void RefreshSource()
    {
        source?.RefreshMetricsCommand.Execute(null);
    }

    /// <summary>Release the shared metric subscription with the shell lifetime.</summary>
    public void Dispose()
    {
        if (source is not null)
        {
            source.PropertyChanged -= SourceChanged;
            preview.RefreshRequested -= RefreshSource;
            source = null;
        }
    }

    private async void ApplyAppearanceClicked(object? sender, RoutedEventArgs args)
    {
        string color = AppearanceAccent.Text?.Trim() ?? "";
        if (color.Length != 7 || color[0] != '#' || !color.Skip(1).All(char.IsAsciiHexDigit))
        {
            AppearanceError.Text = UiText.Get("AppearanceInvalidColor");
            return;
        }
        WidgetAppearance appearance = ReadAppearance(color);
        if (controller is not null)
        {
            await controller.ApplyAppearanceAsync(appearance);
        }
    }

    private async void ResetAppearanceClicked(object? sender, RoutedEventArgs args)
    {
        if (controller is not null)
        {
            await controller.ApplyAppearanceAsync(new WidgetAppearance());
        }
    }

    private async void DragModeClicked(object? sender, RoutedEventArgs args)
    {
        if (controller is not null)
        {
            await controller.SetDragModeAsync(widgetLocked);
        }
    }

    private async void ApplyClicked(object? sender, RoutedEventArgs args)
    {
        var layout = new WidgetLayout((int)(PositionX.Value ?? 80), (int)(PositionY.Value ?? 100),
            (double)(WidgetWidth.Value ?? 300), (double)(WidgetHeight.Value ?? 210), Locked.IsChecked == true);
        if (controller is not null)
        {
            await controller.ApplyAsync(layout);
        }
    }

    private void RetryClicked(object? sender, RoutedEventArgs args)
    {
        controller?.Restore();
    }
}

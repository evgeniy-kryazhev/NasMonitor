using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using NasMonitor.App.Localization;
using NasMonitor.App.Services;
using NasMonitor.App.ViewModels;
using NasMonitor.Core.Profiles;
using NasMonitor.Core.Widgets;

namespace NasMonitor.App.Views.Pages;

/// <summary>Widget placement controls, separate from connection editing and navigation.</summary>
public partial class WidgetSettingsView : UserControl, IDisposable
{
    private readonly WidgetViewModel preview = new();
    private WidgetController? controller;
    private bool populatingAppearance;
    private WidgetViewModel? source;
    private bool switchingWidget;
    private bool synchronizingAccentColor;
    private bool widgetLocked = true;

    /// <summary>Create widget settings controls.</summary>
    public WidgetSettingsView()
    {
        InitializeComponent();
        AppearancePreview.DataContext = preview;
        AppearanceName.TextChanged += (_, _) => UpdatePreview();
        AppearanceAccent.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
            {
                UpdatePreview();
            }
        };
        AppearanceColorPicker.ColorChanged += AccentColorChanged;
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

    internal void Initialize(WidgetController widgetController, WidgetViewModel viewModel)
    {
        switchingWidget = true;
        Dispose();
        controller = widgetController;
        SettingsTabs.IsEnabled = true;
        SourceControls.IsEnabled = true;
        source = viewModel;
        source.PropertyChanged += SourceChanged;
        preview.RefreshRequested += RefreshSource;
        DataContext = viewModel;
        CopyMetrics();
        switchingWidget = false;
    }

    private void SourceSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!switchingWidget && source is not null)
        {
            source.SelectedNas = SourceSelector.SelectedItem as NasProfile;
        }
    }

    internal void InitializeWorkspace(WidgetWorkspace workspace)
    {
        WidgetManagement.DataContext = workspace;
        SettingsTabs.IsEnabled = false;
        SourceControls.IsEnabled = false;
    }

    internal void ClearWidget()
    {
        Dispose();
        controller = null;
        DataContext = null;
        SettingsTabs.IsEnabled = false;
        SourceControls.IsEnabled = false;
    }

    internal WidgetAppearance CaptureAppearanceDraft()
    {
        return ReadAppearance(preview.CurrentAppearance.AccentColor);
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
        AppearanceColorPicker.Color = Color.Parse(appearance.Normalize().AccentColor);
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

        bool validColor = WidgetAccentColor.TryNormalize(AppearanceAccent.Text, out string color);
        if (validColor && !synchronizingAccentColor)
        {
            synchronizingAccentColor = true;
            try
            {
                AppearanceColorPicker.Color = Color.Parse(color);
            }
            finally
            {
                synchronizingAccentColor = false;
            }
        }

        // A partially typed HEX must not reset the rest of the draft or the last valid color.
        WidgetAppearance draft = ReadAppearance(validColor ? color : preview.CurrentAppearance.AccentColor);
        preview.ApplyAppearance(draft);
        AppearanceError.Text = validColor ? "" : UiText.Get("AppearanceInvalidColor");
    }

    private void AccentColorChanged(object? sender, ColorChangedEventArgs args)
    {
        if (populatingAppearance || synchronizingAccentColor)
        {
            return;
        }

        Color color = args.NewColor;
        AppearanceAccent.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        // TextChanged can be deferred; the preview must also update while dragging in the picker.
        UpdatePreview();
    }

    private WidgetAppearance ReadAppearance(string color)
    {
        return new WidgetAppearance(AppearanceName.Text ?? "",
            color,
            ReadNumber(AppearanceOpacity, 100) / 100,
            ReadNumber(AppearanceCorners, 14),
            AppearanceShowCpu.IsChecked == true,
            AppearanceShowRam.IsChecked == true,
            AppearanceShowUpdate.IsChecked == true,
            (WidgetTheme)Math.Max(0, AppearanceTheme.SelectedIndex),
            AppearanceShowStorage.IsChecked == true,
            AppearanceShowNetwork.IsChecked == true,
            AppearanceShowTemperature.IsChecked == true,
            AppearanceShowUptime.IsChecked == true);
    }

    private static double ReadNumber(NumericUpDown control, decimal fallback)
    {
        if (decimal.TryParse(control.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal parsed))
        {
            return (double)parsed;
        }

        return (double)(control.Value ?? fallback);
    }

    private void SourceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WidgetViewModel.MetricsRevision))
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

    private async void ApplyAppearanceClicked(object? sender, RoutedEventArgs args)
    {
        if (!WidgetAccentColor.TryNormalize(AppearanceAccent.Text, out string color))
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
        var layout = new WidgetLayout((int)(PositionX.Value ?? 80),
            (int)(PositionY.Value ?? 100),
            (double)(WidgetWidth.Value ?? 300),
            (double)(WidgetHeight.Value ?? 210),
            Locked.IsChecked == true);
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

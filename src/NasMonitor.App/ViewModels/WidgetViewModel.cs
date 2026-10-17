using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NasMonitor.App.Localization;
using NasMonitor.Core.Connections;
using NasMonitor.Core.Diagnostics;
using NasMonitor.Core.Monitoring;
using NasMonitor.Core.Profiles;
using NasMonitor.Core.Widgets;

namespace NasMonitor.App.ViewModels;

/// <summary>Shared display state without any native platform APIs.</summary>
public partial class WidgetViewModel : ObservableObject
{
    [ObservableProperty]
    private IBrush accentBrush = new SolidColorBrush(Color.Parse("#35B89A"));

    [ObservableProperty]
    private double cpu;

    [ObservableProperty]
    private string cpuDetail = "";

    [ObservableProperty]
    private string cpuText = "—";

    [ObservableProperty]
    private string deviceDescription = "";

    [ObservableProperty]
    private string downloadDetail = "";

    [ObservableProperty]
    private string downloadText = "—";

    [ObservableProperty]
    private string hostStatus = UiText.Get("HostWaiting");

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private string lastUpdate = "—";

    [ObservableProperty]
    private long metricsRevision;

    [ObservableProperty]
    private decimal minimumWidgetHeight = 72;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMonitorDetail))]
    private string monitorDetail = "";

    [ObservableProperty]
    private string monitorStatus = UiText.Get("MonitorNoSource");

    [ObservableProperty]
    private double ram;

    [ObservableProperty]
    private string ramDetail = "";

    [ObservableProperty]
    private string ramText = "—";

    [ObservableProperty]
    private NasProfile? selectedNas;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResourceSection))]
    private bool showCpu = true;

    [ObservableProperty]
    private bool showLastUpdate = true;

    [ObservableProperty]
    private bool showNetwork = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResourceSection))]
    private bool showRam = true;

    [ObservableProperty]
    private bool showStorage = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSystemSection))]
    private bool showTemperature = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSystemSection))]
    private bool showUptime = true;

    [ObservableProperty]
    private string storageDetail = "";

    [ObservableProperty]
    private string storageStatus = "";

    [ObservableProperty]
    private string storageText = "—";

    [ObservableProperty]
    private double storageUsage;

    [ObservableProperty]
    private CornerRadius surfaceCornerRadius = new(14);

    [ObservableProperty]
    private double surfaceOpacity = 1;

    [ObservableProperty]
    private string temperatureDetail = "";

    [ObservableProperty]
    private string temperatureText = "—";

    [ObservableProperty]
    private string uploadDetail = "";

    [ObservableProperty]
    private string uploadText = "—";

    [ObservableProperty]
    private string uptimeDetail = "";

    [ObservableProperty]
    private string uptimeText = "—";

    [ObservableProperty]
    private ThemeVariant widgetThemeVariant = ThemeVariant.Default;

    [ObservableProperty]
    private string widgetTitle = "NAS";

    internal WidgetAppearance CurrentAppearance
    {
        get;
        private set;
    } = new();

    internal NasMonitorState? DisplayState
    {
        get;
        private set;
    }

    internal DemoSnapshot? LastDemoSnapshot
    {
        get;
        private set;
    }

    /// <summary>Saved NAS choices for the widget, independent of the connection editor selection.</summary>
    public ObservableCollection<NasProfile> AvailableNas
    {
        get;
    } = [];

    /// <summary>Collapse a resource row when both metrics are hidden.</summary>
    public bool ShowResourceSection => ShowCpu || ShowRam;

    /// <summary>Collapse a system row when both metrics are hidden.</summary>
    public bool ShowSystemSection => ShowTemperature || ShowUptime;

    /// <summary>Hide the empty diagnostics row in widget settings.</summary>
    public bool HasMonitorDetail => !string.IsNullOrEmpty(MonitorDetail);

    /// <summary>Publish validated presentation settings to the widget and shared preview.</summary>
    public void ApplyAppearance(WidgetAppearance requested)
    {
        WidgetAppearance appearance = requested.Normalize();
        CurrentAppearance = appearance;
        UpdateTitle();
        AccentBrush = new SolidColorBrush(Color.Parse(appearance.AccentColor));
        SurfaceOpacity = appearance.Opacity;
        SurfaceCornerRadius = new CornerRadius(appearance.CornerRadius);
        ShowCpu = appearance.ShowCpu;
        ShowRam = appearance.ShowRam;
        ShowLastUpdate = appearance.ShowLastUpdate;
        ShowStorage = appearance.ShowStorage;
        ShowNetwork = appearance.ShowNetwork;
        ShowTemperature = appearance.ShowTemperature;
        ShowUptime = appearance.ShowUptime;
        WidgetThemeVariant = appearance.Theme switch
        {
            WidgetTheme.Light => ThemeVariant.Light,
            WidgetTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    /// <summary>Publish the single synthetic sample to all bound views.</summary>
    public void Update(DemoSnapshot snapshot)
    {
        LastDemoSnapshot = snapshot;
        Cpu = snapshot.Cpu;
        Ram = snapshot.Ram;
        CpuText = Cpu.ToString("F0", CultureInfo.CurrentCulture) + "%";
        RamText = Ram.ToString("F0", CultureInfo.CurrentCulture) + "%";
        MonitorStatus = "DEMO";
        StorageUsage = 64;
        StorageText = "64%";
        DownloadText = "2.4 MB/s";
        UploadText = "850 KB/s";
        TemperatureText = "39 °C";
        UptimeText = "14 " + UiText.Get("UptimeDays");
        LastUpdate = string.Format(CultureInfo.CurrentCulture,
            UiText.Get("LastUpdateFormat"),
            snapshot.CollectedAt.ToLocalTime().ToString("HH:mm:ss"));
        MetricsRevision++;
    }

    /// <summary>Request a shared refresh, coalesced by the monitoring coordinator.</summary>
    public event Action? RefreshRequested;

    [RelayCommand]
    private void RefreshMetrics()
    {
        RefreshRequested?.Invoke();
    }

    private void UpdateTitle()
    {
        WidgetTitle = CurrentAppearance.Title is "NAS" or "NAS · DEMO"
            ? SelectedNas?.Name ?? "NAS"
            : CurrentAppearance.Title;
    }

    /// <summary>Display only fresh available metrics; the cache retains stale samples for diagnostics.</summary>
    public void ApplyMonitoringState(NasMonitorState? state, DateTimeOffset now)
    {
        LastDemoSnapshot = null;
        DisplayState = state;
        UpdateTitle();
        MonitorStatus = UiText.Get(state is null ? "MonitorNoSource" : "Monitor" + state.Status);
        MonitorDetail = state?.Error is { } error && error != ConnectionError.None ? UiText.Get("Error" + error) : "";
        if (state?.Status == NasMonitorStatus.Degraded && string.IsNullOrEmpty(MonitorDetail))
        {
            MonitorDetail = UiText.Get("MonitorPartialHelp");
        }

        NasSnapshot? snapshot = state?.Snapshot;
        bool stale = state?.IsStale == true;
        double? cpuValue = FreshValue(snapshot?.Cpu, now, stale, 35);
        double? ramValue = FreshValue(snapshot?.Ram, now, stale, 35);
        double? storageValue = FreshValue(snapshot?.Storage, now, stale, 95);
        Cpu = cpuValue ?? 0;
        Ram = ramValue ?? 0;
        StorageUsage = storageValue ?? 0;
        CpuText = Percent(cpuValue);
        RamText = Percent(ramValue);
        StorageText = Percent(storageValue);
        DownloadText = FormatRate(FreshValue(snapshot?.DownloadBytesPerSecond, now, stale, 35));
        UploadText = FormatRate(FreshValue(snapshot?.UploadBytesPerSecond, now, stale, 35));
        double? temperature = FreshValue(snapshot?.TemperatureCelsius, now, stale, 95);
        TemperatureText = temperature is null
            ? "—"
            : temperature.Value.ToString("F0", CultureInfo.CurrentCulture) + " °C";
        double? uptime = FreshValue(snapshot?.UptimeSeconds, now, stale, 95);
        UptimeText = FormatUptime(uptime);
        CpuDetail = MetricDetail(snapshot?.Cpu, now, stale, 35);
        RamDetail = MetricDetail(snapshot?.Ram, now, stale, 35);
        StorageDetail = MetricDetail(snapshot?.Storage, now, stale, 95);
        DownloadDetail = MetricDetail(snapshot?.DownloadBytesPerSecond, now, stale, 35);
        UploadDetail = MetricDetail(snapshot?.UploadBytesPerSecond, now, stale, 35);
        TemperatureDetail = MetricDetail(snapshot?.TemperatureCelsius, now, stale, 95);
        UptimeDetail = MetricDetail(snapshot?.UptimeSeconds, now, stale, 95);
        DeviceDescription = string.Join(" · ",
            new[] { snapshot?.Model, snapshot?.DsmVersion }.Where(text => !string.IsNullOrWhiteSpace(text)));
        LastUpdate = state?.LastSuccessAt is { } lastSuccess
            ? string.Format(CultureInfo.CurrentCulture,
                UiText.Get(stale ? "LastSuccessFormat" : "LastUpdateFormat"),
                lastSuccess.ToLocalTime().ToString("HH:mm:ss"))
            : "—";
        if (state?.Status is NasMonitorStatus.Online or NasMonitorStatus.Degraded &&
            snapshot is not null && now - snapshot.CollectedAt > TimeSpan.FromSeconds(35))
        {
            MonitorStatus = UiText.Get("MonitorStale");
        }

        MetricsRevision++;
    }

    private static double? FreshValue(NasMetric? metric, DateTimeOffset now, bool stale, int maximumAgeSeconds)
    {
        return !stale && metric?.Availability == MetricAvailability.Available &&
               now - metric.CollectedAt <= TimeSpan.FromSeconds(maximumAgeSeconds)
            ? metric.Value
            : null;
    }

    private static string MetricDetail(NasMetric? metric, DateTimeOffset now, bool stale, int maximumAgeSeconds)
    {
        MetricAvailability availability = metric?.Availability ?? MetricAvailability.Unavailable;
        if (availability == MetricAvailability.Available &&
            (stale || now - metric!.CollectedAt > TimeSpan.FromSeconds(maximumAgeSeconds)))
        {
            availability = MetricAvailability.Stale;
        }

        return UiText.Get("Metric" + availability);
    }

    private static string Percent(double? value)
    {
        return value is null ? "—" : value.Value.ToString("F0", CultureInfo.CurrentCulture) + "%";
    }

    private static string FormatRate(double? bytesPerSecond)
    {
        if (bytesPerSecond is null)
        {
            return "—";
        }

        double value = bytesPerSecond.Value;
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s", "TB/s"];
        int unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return value.ToString(value >= 100 || unit == 0 ? "F0" : "F1", CultureInfo.CurrentCulture) + " " + units[unit];
    }

    private static string FormatUptime(double? seconds)
    {
        if (seconds is null)
        {
            return "—";
        }

        var duration = TimeSpan.FromSeconds(seconds.Value);
        if (duration.TotalDays >= 1)
        {
            return duration.Days.ToString(CultureInfo.CurrentCulture) + " " + UiText.Get("UptimeDays");
        }

        return duration.Hours.ToString(CultureInfo.CurrentCulture) + " " + UiText.Get("UptimeHours") + " " +
               duration.Minutes.ToString(CultureInfo.CurrentCulture) + " " + UiText.Get("UptimeMinutes");
    }
}

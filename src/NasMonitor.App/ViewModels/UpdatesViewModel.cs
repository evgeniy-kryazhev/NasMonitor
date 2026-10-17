using System.ComponentModel;
using System.Net;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NasMonitor.App.Localization;
using NasMonitor.App.Persistence;
using NasMonitor.Core.Updates;

namespace NasMonitor.App.ViewModels;

/// <summary>Shows update state and schedules checks without platform APIs or NAS dependencies.</summary>
public sealed partial class UpdatesViewModel : ObservableObject, IDisposable
{
    private readonly string? assetName;
    private readonly IReleaseUpdateClient client;
    private readonly IAppUpdateInstaller installer;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private readonly bool networkEnabled;
    private readonly UpdatePreferencesStore preferences;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };

    [ObservableProperty]
    private bool automaticChecks = true;

    private bool disposed;
    private bool initialized;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool isBusy;

    [ObservableProperty]
    private bool isChecking;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableVersion))]
    [NotifyPropertyChangedFor(nameof(ReleaseNotes))]
    [NotifyPropertyChangedFor(nameof(NavigationLabel))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private UpdateRelease? release;

    [ObservableProperty]
    private string status = UiText.Get("UpdateReady");

    /// <summary>Bind the release client, installer and non-secret preference store.</summary>
    public UpdatesViewModel(IReleaseUpdateClient client,
        IAppUpdateInstaller installer,
        UpdatePreferencesStore preferences,
        Version currentVersion,
        string? assetName,
        bool networkEnabled = true)
    {
        this.client = client;
        this.installer = installer;
        this.preferences = preferences;
        this.assetName = assetName;
        this.networkEnabled = networkEnabled;
        CurrentVersion = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
        lifetimeToken = lifetime.Token;
        timer.Tick += AutomaticCheck;
    }

    /// <summary>The running assembly version, compared with stable release tags.</summary>
    public Version CurrentVersion
    {
        get;
    }

    /// <summary>Version offered by GitHub for this exact platform.</summary>
    public string AvailableVersion => Release?.Version.ToString(3) ?? "—";

    /// <summary>Release notes displayed as plain text, never executed as HTML.</summary>
    public string ReleaseNotes => Release?.Notes ?? "";

    /// <summary>Indicate an available version even while another page is selected.</summary>
    public string NavigationLabel => UiText.Get("NavigationUpdates") + (Release is null ? "" : " •");

    /// <summary>Whether this running package can be replaced in place.</summary>
    public bool IsPortable => installer.CanInstall;

    private bool CanCheck => !disposed && !IsBusy && assetName is not null && networkEnabled;
    private bool CanInstall => CanCheck && Release is not null && installer.CanInstall;

    /// <summary>Stop periodic checks and cancel downloads without changing NAS sessions.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= AutomaticCheck;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    /// <summary>Signals that the helper is ready and the main process can shut down cleanly.</summary>
    public event Action? RestartRequested;

    /// <summary>Load preferences before the first automatic request; smoke makes no network calls.</summary>
    public async Task StartAsync()
    {
        try
        {
            AutomaticChecks = await preferences.LoadAsync(lifetimeToken);
            string? result = await preferences.ReadInstallResultAsync(lifetimeToken);
            if (result is not null)
            {
                Status = UiText.Get(result);
            }

            initialized = true;
            if (!networkEnabled)
            {
                Status = UiText.Get("UpdateSmokeMode");
            }
            else if (assetName is null)
            {
                Status = UiText.Get("UpdateUnsupported");
            }
            else if (AutomaticChecks && !disposed)
            {
                timer.Start();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    partial void OnAutomaticChecksChanged(bool value)
    {
        if (!initialized || disposed)
        {
            return;
        }

        timer.Stop();
        if (value && networkEnabled && assetName is not null)
        {
            timer.Interval = TimeSpan.FromSeconds(5);
            timer.Start();
        }

        _ = SavePreferenceAsync(value);
    }

    private async Task SavePreferenceAsync(bool value)
    {
        try
        {
            if (!await preferences.SaveAsync(value, lifetimeToken))
            {
                Status = UiText.Get("LayoutSaveFailed");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void AutomaticCheck(object? sender, EventArgs args)
    {
        timer.Interval = TimeSpan.FromHours(6);
        if (AutomaticChecks && CanCheck)
        {
            await CheckCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        IsBusy = true;
        IsChecking = true;
        Status = UiText.Get("UpdateChecking");
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            operation.CancelAfter(TimeSpan.FromSeconds(20));
            UpdateCheckResult result = await client.CheckAsync(CurrentVersion, assetName!, operation.Token);
            Release = result.Release;
            Status = UiText.Get("Update" + result.Status);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Forbidden
                                                         or HttpStatusCode.TooManyRequests)
        {
            Status = UiText.Get("UpdateRateLimited");
        }
        catch (OperationCanceledException)
        {
            Status = UiText.Get("UpdateCheckFailed");
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Status = UiText.Get("UpdateCheckFailed");
        }
        finally
        {
            IsChecking = false;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall), IncludeCancelCommand = true)]
    private async Task InstallAsync(CancellationToken cancellationToken)
    {
        if (Release is not { } selected)
        {
            return;
        }

        IsBusy = true;
        Progress = 0;
        Status = UiText.Get("UpdateDownloading");
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, cancellationToken);
            operation.CancelAfter(TimeSpan.FromMinutes(10));
            var progress = new Progress<double>(value => Progress = value);
            DownloadedUpdate downloaded = await client.DownloadAsync(selected, progress, operation.Token);
            Status = UiText.Get("UpdatePreparing");
            await installer.PrepareAsync(downloaded, operation.Token);
            RestartRequested?.Invoke();
        }
        catch (OperationCanceledException)
        {
            Status = UiText.Get("UpdateCancelled");
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Status = UiText.Get("UpdateInstallFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is HttpRequestException or IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or ArgumentException or
            Win32Exception or NotSupportedException or KeyNotFoundException or FormatException;
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NasMonitor.App.Localization;
using NasMonitor.App.Models;
using NasMonitor.App.Services;
using NasMonitor.Core.Connections;
using NasMonitor.Core.Monitoring;
using NasMonitor.Core.Profiles;
using NasMonitor.Infrastructure.Profiles;

namespace NasMonitor.App.ViewModels;

/// <summary>Connection editor and check results; contains no native or direct HTTP calls.</summary>
public partial class NasConnectionsViewModel(
    NasProfileService profiles,
    INasConnectionTester tester,
    WidgetMonitoringPresenter monitoring) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();

    [ObservableProperty]
    private string capabilitySummary = "";

    private bool disposed;
    private Guid editingId = Guid.NewGuid();

    [ObservableProperty]
    private bool enabled = true;

    [ObservableProperty]
    private string endpoint = "";

    [ObservableProperty]
    private string externalEndpoint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(NewProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectMonitorCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string otp = "";

    [ObservableProperty]
    private string password = "";

    [ObservableProperty]
    private string profileName = "";

    [ObservableProperty]
    private bool rememberPassword;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectMonitorCommand))]
    private NasProfile? selectedProfile;

    [ObservableProperty]
    private string status = UiText.Get("NasReady");

    [ObservableProperty]
    private bool useExternalEndpoint;

    [ObservableProperty]
    private string username = "";

    /// <summary>Saved, independent NAS profiles.</summary>
    public ObservableCollection<NasProfile> Profiles
    {
        get;
    } = [];

    /// <summary>Localized stage results from the latest explicit connection check.</summary>
    public ObservableCollection<ConnectionCheckRow> Checks
    {
        get;
    } = [];

    /// <summary>Prevent edits and competing checks while an operation is running.</summary>
    public bool CanEdit => !IsBusy;

    private bool CanDelete => CanEdit && SelectedProfile is not null;

    /// <summary>Cancel network work and clear secret fields when the shell closes.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();
        Password = "";
        Otp = "";
        lifetime.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanDelete), IncludeCancelCommand = true)]
    private async Task ConnectMonitorAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Status = UiText.Get("MonitorConnecting");
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        try
        {
            NasProfile requested = ReadForm();
            NasProfile? saved = Profiles.FirstOrDefault(profile => profile.Id == requested.Id);
            if (saved is null || saved with { CredentialId = null } != requested)
            {
                Status = UiText.Get("MonitorSaveFirst");
                return;
            }

            Uri target = UseExternalEndpoint
                ? saved.ExternalEndpoint ?? throw new ArgumentException("endpoint.external-missing")
                : saved.Endpoint;
            NasMonitorState state = await monitoring.ConnectAsync(saved, target, Password, Otp, operation.Token);
            Status = UiText.Get("Monitor" + state.Status);
            if (state.Error != ConnectionError.None)
            {
                Status += ": " + UiText.Get("Error" + state.Error);
            }
        }
        catch (OperationCanceledException)
        {
            Status = UiText.Get("ErrorCancelled");
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Status = UiText.Get(exception is ArgumentException ? "NasInvalidProfile" : "NasStorageFailed");
        }
        finally
        {
            Password = "";
            Otp = "";
            IsBusy = false;
        }
    }

    /// <summary>Load metadata without reading any saved password.</summary>
    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await ReloadAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Status = UiText.Get("NasStorageFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedProfileChanged(NasProfile? value)
    {
        Password = "";
        Otp = "";
        UseExternalEndpoint = false;
        Checks.Clear();
        CapabilitySummary = "";
        if (value is null)
        {
            return;
        }

        editingId = value.Id;
        ProfileName = value.Name;
        Endpoint = value.Endpoint.AbsoluteUri;
        ExternalEndpoint = value.ExternalEndpoint?.AbsoluteUri ?? "";
        Username = value.Username;
        Enabled = value.Enabled;
        RememberPassword = value.CredentialId.HasValue;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void NewProfile()
    {
        SelectedProfile = null;
        editingId = Guid.NewGuid();
        ProfileName = "";
        Endpoint = "";
        ExternalEndpoint = "";
        Username = "";
        Password = "";
        Otp = "";
        Enabled = true;
        RememberPassword = false;
        UseExternalEndpoint = false;
        Checks.Clear();
        CapabilitySummary = "";
        Status = UiText.Get("NasReady");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveProfileAsync()
    {
        IsBusy = true;
        try
        {
            NasProfile requested = ReadForm();
            NasProfile saved = await profiles.SaveAsync(requested, Password, RememberPassword, lifetime.Token);
            await ReloadAsync(lifetime.Token);
            SelectedProfile = Profiles.First(profile => profile.Id == saved.Id);
            Status = UiText.Get("NasSaved");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Status = UiText.Get(exception is ArgumentException ? "NasInvalidProfile" : "NasStorageFailed");
        }
        finally
        {
            Password = "";
            Otp = "";
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } selected)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await profiles.DeleteAsync(selected, lifetime.Token);
            await ReloadAsync(lifetime.Token);
            NewProfile();
            Status = UiText.Get("NasDeleted");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Status = UiText.Get("NasStorageFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit), IncludeCancelCommand = true)]
    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Checks.Clear();
        CapabilitySummary = "";
        Status = UiText.Get("NasChecking");
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        bool acceptingProgress = true;
        try
        {
            NasProfile profile = ReadForm();
            string? suppliedPassword = Password;
            if (string.IsNullOrEmpty(suppliedPassword))
            {
                suppliedPassword = await profiles.ReadPasswordAsync(profile, operation.Token);
            }

            if (UseExternalEndpoint)
            {
                if (profile.ExternalEndpoint is null)
                {
                    throw new ArgumentException("endpoint.external-missing");
                }

                profile = profile with { Endpoint = profile.ExternalEndpoint };
            }

            var progress = new Progress<ConnectionCheck>(check =>
            {
                if (acceptingProgress)
                {
                    Checks.Add(ConnectionCheckRow.From(check));
                }
            });
            NasConnectionResult result =
                await tester.TestAsync(profile, suppliedPassword ?? "", Otp, progress, operation.Token);
            acceptingProgress = false;
            // Replace progress with the authoritative ordered result after all stages finish.
            Checks.Clear();
            foreach (ConnectionCheck check in result.Checks)
            {
                Checks.Add(ConnectionCheckRow.From(check));
            }

            Status = UiText.Get(result.Authenticated ? "NasAuthenticated" : "NasCheckFailed");
            CapabilitySummary = string.Join(", ", result.Capabilities.Select(capability => capability.Api));
        }
        catch (OperationCanceledException)
        {
            Status = UiText.Get("ErrorCancelled");
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Status = UiText.Get(exception is ArgumentException ? "NasInvalidProfile" : "NasStorageFailed");
        }
        finally
        {
            acceptingProgress = false;
            Password = "";
            Otp = "";
            IsBusy = false;
        }
    }

    private NasProfile ReadForm()
    {
        var primary = new Uri(Endpoint.Trim(), UriKind.Absolute);
        Uri? external = string.IsNullOrWhiteSpace(ExternalEndpoint)
            ? null
            : new Uri(ExternalEndpoint.Trim(), UriKind.Absolute);
        var profile = new NasProfile(editingId,
            ProfileName.Trim(),
            primary,
            Username.Trim(),
            Enabled,
            ExternalEndpoint: external);
        profile.Validate();
        return profile;
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NasProfile> saved = await profiles.LoadAsync(cancellationToken);
        Profiles.Clear();
        foreach (NasProfile profile in saved.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Profiles.Add(profile);
        }

        await monitoring.SynchronizeAsync(saved, cancellationToken);
    }

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or UriFormatException or Win32Exception or InvalidOperationException
            or
            NotSupportedException;
    }

    /// <summary>Clear credentials and cancel a check when navigating away.</summary>
    public void LeavePage()
    {
        TestConnectionCommand.Cancel();
        ConnectMonitorCommand.Cancel();
        Password = "";
        Otp = "";
    }
}

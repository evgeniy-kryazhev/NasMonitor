using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NasMonitor.Core;
using NasMonitor.Infrastructure;

namespace NasMonitor.App;

/// <summary>Connection editor and check results; contains no native or direct HTTP calls.</summary>
public partial class NasConnectionsViewModel(NasProfileService profiles, INasConnectionTester tester,
    WidgetMonitoringPresenter monitoring) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private Guid editingId = Guid.NewGuid();
    private bool disposed;

    /// <summary>Saved, independent NAS profiles.</summary>
    public ObservableCollection<NasProfile> Profiles { get; } = [];
    /// <summary>Localized stage results from the latest explicit connection check.</summary>
    public ObservableCollection<ConnectionCheckRow> Checks { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectMonitorCommand))]
    private NasProfile? selectedProfile;
    [ObservableProperty] private string profileName = "";
    [ObservableProperty] private string endpoint = "";
    [ObservableProperty] private string externalEndpoint = "";
    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string otp = "";
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private bool rememberPassword;
    [ObservableProperty] private bool useExternalEndpoint;
    [ObservableProperty] private string status = UiText.Get("NasReady");
    [ObservableProperty] private string capabilitySummary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(NewProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectMonitorCommand))]
    private bool isBusy;

    /// <summary>Prevent edits and competing checks while an operation is running.</summary>
    public bool CanEdit => !IsBusy;

    private bool CanDelete => CanEdit && SelectedProfile is not null;

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
            NasConnectionResult result = await tester.TestAsync(profile, suppliedPassword ?? "", Otp, progress, operation.Token);
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
        Uri? external = string.IsNullOrWhiteSpace(ExternalEndpoint) ? null : new Uri(ExternalEndpoint.Trim(), UriKind.Absolute);
        var profile = new NasProfile(editingId, ProfileName.Trim(), primary, Username.Trim(), Enabled,
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
        return exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or
            ArgumentException or UriFormatException or System.ComponentModel.Win32Exception or InvalidOperationException or
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
}

/// <summary>Localized, non-secret check evidence displayed in the editor.</summary>
public sealed record ConnectionCheckRow(string Stage, string Detail)
{
    /// <summary>Render controlled stage/error identifiers through RU resources.</summary>
    public static ConnectionCheckRow From(ConnectionCheck check)
    {
        string detail = check.State == CheckState.Failed
            ? UiText.Get("Error" + check.Error)
            : UiText.Get("Check" + check.State);
        if (check.DsmCode.HasValue)
        {
            detail += " (DSM " + check.DsmCode.Value + ")";
        }
        return new(UiText.Get("Stage" + check.Stage), detail);
    }
}

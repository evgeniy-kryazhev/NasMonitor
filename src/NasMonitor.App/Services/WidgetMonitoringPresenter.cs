using System.ComponentModel;
using Avalonia.Threading;
using NasMonitor.App.Localization;
using NasMonitor.App.Persistence;
using NasMonitor.App.ViewModels;
using NasMonitor.Core.Monitoring;
using NasMonitor.Core.Profiles;
using NasMonitor.Infrastructure.Monitoring;

namespace NasMonitor.App.Services;

/// <summary>Projects one shared NAS cache onto independently selected widget sources.</summary>
public sealed class WidgetMonitoringPresenter : IDisposable
{
    private readonly NasMonitoringCoordinator monitoring;
    private readonly bool networkEnabled;
    private readonly Dictionary<WidgetViewModel, Subscription> subscriptions = new();
    private bool disposed;
    private IReadOnlyList<NasProfile> profiles = [];

    /// <summary>Subscribe to the shared coordinator without owning any NAS sessions.</summary>
    public WidgetMonitoringPresenter(NasMonitoringCoordinator monitoring, bool networkEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(monitoring);
        this.monitoring = monitoring;
        this.networkEnabled = networkEnabled;
        monitoring.StateChanged += StateChanged;
    }

    /// <summary>The widget targeted by an explicit connection from the DSM page.</summary>
    public WidgetViewModel? ActiveWidget
    {
        get;
        set;
    }

    /// <summary>Release all UI subscriptions before the shared coordinator shuts down.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        monitoring.StateChanged -= StateChanged;
        foreach (WidgetViewModel viewModel in subscriptions.Keys.ToArray())
        {
            Unregister(viewModel);
        }
    }

    /// <summary>Restore one widget source and attach its refresh and selection subscriptions.</summary>
    public async Task RegisterAsync(WidgetViewModel viewModel,
        WidgetSettingsStore storage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(storage);
        ObjectDisposedException.ThrowIf(disposed, this);
        Guid? selectedId = await storage.LoadNasSelectionAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        var subscription = new Subscription(storage, selectedId, () => RequestRefresh(viewModel));
        subscriptions.Add(viewModel, subscription);
        viewModel.PropertyChanged += SelectionChanged;
        viewModel.RefreshRequested += subscription.Refresh;
        ApplyProfiles(viewModel, subscription);
        Refresh(viewModel);
    }

    /// <summary>Detach one widget while leaving every NAS poller running for other consumers.</summary>
    public void Unregister(WidgetViewModel viewModel)
    {
        if (!subscriptions.Remove(viewModel, out Subscription? subscription))
        {
            return;
        }

        viewModel.PropertyChanged -= SelectionChanged;
        viewModel.RefreshRequested -= subscription.Refresh;
        subscription.Lifetime.Cancel();
        subscription.Lifetime.Dispose();
        if (ReferenceEquals(ActiveWidget, viewModel))
        {
            ActiveWidget = null;
        }
    }

    /// <summary>Update all source choices and synchronize each NAS poller once after profile changes.</summary>
    public async Task SynchronizeAsync(IReadOnlyList<NasProfile> configuredProfiles,
        CancellationToken cancellationToken)
    {
        profiles = configuredProfiles.OrderBy(profile => profile.Name,
            StringComparer.CurrentCultureIgnoreCase).ToArray();
        foreach ((WidgetViewModel viewModel, Subscription subscription) in subscriptions)
        {
            ApplyProfiles(viewModel, subscription);
        }

        if (networkEnabled)
        {
            await monitoring.SynchronizeAsync(configuredProfiles, cancellationToken);
        }

        Refresh();
    }

    private void ApplyProfiles(WidgetViewModel viewModel, Subscription subscription)
    {
        subscription.Synchronizing = true;
        try
        {
            viewModel.AvailableNas.Clear();
            foreach (NasProfile profile in profiles)
            {
                viewModel.AvailableNas.Add(profile);
            }

            // A deleted NAS leaves an empty source instead of silently showing another device.
            viewModel.SelectedNas = profiles.FirstOrDefault(profile => profile.Id == subscription.SelectedId);
        }
        finally
        {
            subscription.Synchronizing = false;
        }
    }

    /// <summary>Connect a saved profile and assign it only to the widget selected before login.</summary>
    public async Task<NasMonitorState> ConnectAsync(NasProfile profile,
        Uri endpoint,
        string? password,
        string? otp,
        CancellationToken cancellationToken)
    {
        WidgetViewModel? target = ActiveWidget;
        if (target is not null && subscriptions.ContainsKey(target))
        {
            target.SelectedNas = target.AvailableNas.FirstOrDefault(item => item.Id == profile.Id);
        }

        return await monitoring.ConnectAsync(profile, endpoint, password, otp, cancellationToken);
    }

    /// <summary>Recompute every widget from cached data without issuing HTTP requests.</summary>
    public void Refresh()
    {
        foreach (WidgetViewModel viewModel in subscriptions.Keys)
        {
            Refresh(viewModel);
        }
    }

    /// <summary>Update one widget, keeping its metric age independent of the editor selection.</summary>
    public void Refresh(WidgetViewModel viewModel)
    {
        if (disposed || !networkEnabled)
        {
            return;
        }

        NasMonitorState? state = viewModel.SelectedNas is { } selected ? monitoring.GetState(selected.Id) : null;
        viewModel.ApplyMonitoringState(state, DateTimeOffset.UtcNow);
    }

    private void StateChanged(NasMonitorState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (disposed)
            {
                return;
            }

            foreach (WidgetViewModel viewModel in subscriptions.Keys)
            {
                if (viewModel.SelectedNas?.Id == state.NasId)
                {
                    Refresh(viewModel);
                }
            }
        });
    }

    private void SelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(WidgetViewModel.SelectedNas) || sender is not WidgetViewModel viewModel ||
            !subscriptions.TryGetValue(viewModel, out Subscription? subscription) || subscription.Synchronizing ||
            disposed)
        {
            return;
        }

        subscription.SelectedId = viewModel.SelectedNas?.Id;
        Refresh(viewModel);
        subscription.PendingSave =
            SaveSourceAsync(subscription.PendingSave, viewModel, subscription, subscription.SelectedId);
    }

    private static async Task SaveSourceAsync(Task previousSave,
        WidgetViewModel viewModel,
        Subscription subscription,
        Guid? selectedId)
    {
        CancellationToken token = subscription.Lifetime.Token;
        try
        {
            // Preserve selection order even when the user switches sources faster than disk writes finish.
            await previousSave;
            bool saved = await subscription.Storage.SaveNasSelectionAsync(selectedId, token);
            if (!saved)
            {
                viewModel.StorageStatus = UiText.Get("LayoutSaveFailed");
            }
        }
        catch (OperationCanceledException)
        {
            // Closing a widget cancels its writes without affecting other subscriptions.
        }
    }

    /// <summary>Wait for the latest source selection to reach the widget's settings store.</summary>
    public Task FlushAsync(WidgetViewModel viewModel, CancellationToken cancellationToken)
    {
        return subscriptions.TryGetValue(viewModel, out Subscription? subscription)
            ? subscription.PendingSave.WaitAsync(cancellationToken)
            : Task.CompletedTask;
    }

    private async void RequestRefresh(WidgetViewModel viewModel)
    {
        if (disposed || !networkEnabled || viewModel.SelectedNas is not { } selected ||
            !subscriptions.TryGetValue(viewModel, out Subscription? subscription))
        {
            return;
        }

        try
        {
            await monitoring.RefreshAsync(selected.Id, subscription.Lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class Subscription(WidgetSettingsStore storage, Guid? selectedId, Action refresh)
    {
        internal WidgetSettingsStore Storage
        {
            get;
        } = storage;

        internal Guid? SelectedId
        {
            get;
            set;
        } = selectedId;

        internal Action Refresh
        {
            get;
        } = refresh;

        internal CancellationTokenSource Lifetime
        {
            get;
        } = new();

        internal Task PendingSave
        {
            get;
            set;
        } = Task.CompletedTask;

        internal bool Synchronizing
        {
            get;
            set;
        }
    }
}

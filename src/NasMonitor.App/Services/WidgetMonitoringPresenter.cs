using System.ComponentModel;
using Avalonia.Threading;
using NasMonitor.App.Localization;
using NasMonitor.App.Persistence;
using NasMonitor.App.ViewModels;
using NasMonitor.Core.Monitoring;
using NasMonitor.Core.Profiles;
using NasMonitor.Infrastructure.Monitoring;

namespace NasMonitor.App.Services;

/// <summary>Projects shared monitor states onto UI state and persists the widget's selected NAS.</summary>
public sealed class WidgetMonitoringPresenter : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly NasMonitoringCoordinator monitoring;
    private readonly bool networkEnabled;
    private readonly WidgetSettingsStore storage;
    private readonly WidgetViewModel viewModel;
    private bool disposed;
    private bool initialized;
    private bool synchronizing;

    /// <summary>Subscribe once for all widget/preview consumers.</summary>
    public WidgetMonitoringPresenter(NasMonitoringCoordinator monitoring,
        WidgetSettingsStore storage,
        WidgetViewModel viewModel,
        bool networkEnabled = true)
    {
        this.monitoring = monitoring;
        this.storage = storage;
        this.viewModel = viewModel;
        this.networkEnabled = networkEnabled;
        monitoring.StateChanged += StateChanged;
        viewModel.PropertyChanged += SelectionChanged;
        viewModel.RefreshRequested += RefreshRequested;
    }

    /// <summary>Release UI subscriptions before monitor shutdown.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        monitoring.StateChanged -= StateChanged;
        viewModel.PropertyChanged -= SelectionChanged;
        viewModel.RefreshRequested -= RefreshRequested;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    /// <summary>Update saved source choices and synchronize pollers after profile changes.</summary>
    public async Task SynchronizeAsync(IReadOnlyList<NasProfile> profiles, CancellationToken cancellationToken)
    {
        Guid? selectedId =
            initialized ? viewModel.SelectedNas?.Id : await storage.LoadNasSelectionAsync(cancellationToken);
        initialized = true;
        synchronizing = true;
        try
        {
            viewModel.AvailableNas.Clear();
            foreach (NasProfile profile in profiles.OrderBy(profile => profile.Name,
                         StringComparer.CurrentCultureIgnoreCase))
            {
                viewModel.AvailableNas.Add(profile);
            }

            viewModel.SelectedNas = viewModel.AvailableNas.FirstOrDefault(profile => profile.Id == selectedId)
                                    ?? viewModel.AvailableNas.FirstOrDefault(profile => profile.Enabled);
        }
        finally
        {
            synchronizing = false;
        }

        if (networkEnabled)
        {
            await monitoring.SynchronizeAsync(profiles, cancellationToken);
        }

        Refresh();
    }

    /// <summary>Connect a saved profile and make it the current widget source.</summary>
    public async Task<NasMonitorState> ConnectAsync(NasProfile profile,
        Uri endpoint,
        string? password,
        string? otp,
        CancellationToken cancellationToken)
    {
        viewModel.SelectedNas = viewModel.AvailableNas.FirstOrDefault(item => item.Id == profile.Id);
        return await monitoring.ConnectAsync(profile, endpoint, password, otp, cancellationToken);
    }

    /// <summary>Recompute metric age from the shared cache without any HTTP work.</summary>
    public void Refresh()
    {
        if (!disposed && networkEnabled)
        {
            NasMonitorState? state = viewModel.SelectedNas is { } selected ? monitoring.GetState(selected.Id) : null;
            viewModel.ApplyMonitoringState(state, DateTimeOffset.UtcNow);
        }
    }

    private void StateChanged(NasMonitorState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && viewModel.SelectedNas?.Id == state.NasId)
            {
                Refresh();
            }
        });
    }

    private async void SelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(WidgetViewModel.SelectedNas) || synchronizing || disposed)
        {
            return;
        }

        Refresh();
        try
        {
            bool saved = await storage.SaveNasSelectionAsync(viewModel.SelectedNas?.Id, lifetime.Token);
            if (!saved)
            {
                viewModel.StorageStatus = UiText.Get("LayoutSaveFailed");
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async void RefreshRequested()
    {
        if (viewModel.SelectedNas is not { } selected || disposed)
        {
            return;
        }

        try
        {
            await monitoring.RefreshAsync(selected.Id, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }
}

using System.ComponentModel;
using Avalonia.Threading;
using NasMonitor.Core;
using NasMonitor.Infrastructure;

namespace NasMonitor.App;

/// <summary>Projects shared monitor states onto UI state and persists the widget's selected NAS.</summary>
public sealed class WidgetMonitoringPresenter : IDisposable
{
    private readonly NasMonitoringCoordinator monitoring;
    private readonly SpikeStorage storage;
    private readonly SpikeViewModel viewModel;
    private readonly CancellationTokenSource lifetime = new();
    private bool initialized;
    private bool synchronizing;
    private bool disposed;
    private readonly bool networkEnabled;

    /// <summary>Subscribe once for all widget/preview consumers.</summary>
    public WidgetMonitoringPresenter(NasMonitoringCoordinator monitoring, SpikeStorage storage, SpikeViewModel viewModel,
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

    /// <summary>Update saved source choices and synchronize pollers after profile changes.</summary>
    public async Task SynchronizeAsync(IReadOnlyList<NasProfile> profiles, CancellationToken cancellationToken)
    {
        Guid? selectedId = initialized ? viewModel.SelectedNas?.Id : await storage.LoadNasSelectionAsync(cancellationToken);
        initialized = true;
        synchronizing = true;
        try
        {
            viewModel.AvailableNas.Clear();
            foreach (NasProfile profile in profiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase))
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
    public async Task<NasMonitorState> ConnectAsync(NasProfile profile, Uri endpoint, string? password, string? otp,
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
        if (args.PropertyName != nameof(SpikeViewModel.SelectedNas) || synchronizing || disposed)
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
}

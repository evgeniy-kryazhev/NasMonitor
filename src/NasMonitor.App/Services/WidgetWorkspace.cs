using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NasMonitor.App.Localization;
using NasMonitor.App.Models;
using NasMonitor.App.Persistence;
using NasMonitor.App.ViewModels;
using NasMonitor.App.Views.Windows;
using NasMonitor.Core.Widgets;
using NasMonitor.Hosting.Abstractions;

namespace NasMonitor.App.Services;

/// <summary>Owns persistent widget membership and the currently edited instance.</summary>
public sealed partial class WidgetWorkspace : ObservableObject, IDisposable
{
    private readonly SemaphoreSlim changes = new(1, 1);
    private readonly bool demoMode;
    private readonly WidgetGestureCoordinator gestures = new();
    private readonly IDesktopWidgetHost host;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private readonly WidgetMonitoringPresenter monitoring;
    private readonly WidgetSettingsStore storage;
    private bool disposed;
    private bool initialized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(CreateWidgetCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteWidgetCommand))]
    private bool isBusy;

    private ManagerWindow? manager;
    private bool restoring;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteWidgetCommand))]
    private WidgetInstance? selectedWidget;

    [ObservableProperty]
    private string status = "";

    /// <summary>Use one native host, settings root and shared monitoring presenter for all windows.</summary>
    public WidgetWorkspace(IDesktopWidgetHost host,
        WidgetSettingsStore storage,
        WidgetMonitoringPresenter monitoring,
        bool demoMode = false)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(monitoring);
        this.host = host;
        this.storage = storage;
        this.monitoring = monitoring;
        this.demoMode = demoMode;
        lifetimeToken = lifetime.Token;
    }

    /// <summary>Runtime windows in their persisted order.</summary>
    public ObservableCollection<WidgetInstance> Widgets
    {
        get;
    } = [];

    /// <summary>Disable competing catalog changes while persistence is in progress.</summary>
    public bool CanEdit => initialized && !IsBusy && !disposed;

    private bool CanDelete => CanEdit && Widgets.Count > 1 && SelectedWidget is not null;

    /// <summary>Close every native window and cancel pending catalog work before monitor shutdown.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();
        foreach (WidgetInstance instance in Widgets)
        {
            instance.Controller.Dispose();
            monitoring.Unregister(instance.ViewModel);
        }

        lifetime.Dispose();
    }

    /// <summary>Raised on the UI thread when the editor must switch to another instance.</summary>
    public event Action<WidgetInstance?>? SelectionChanged;

    partial void OnSelectedWidgetChanged(WidgetInstance? value)
    {
        monitoring.ActiveWidget = value?.ViewModel;
        SelectionChanged?.Invoke(value);
        if (!restoring && !IsBusy && value is not null && !disposed)
        {
            _ = SaveSelectionAsync();
        }
    }

    /// <summary>Restore all windows after the manager has acquired its screen information.</summary>
    public async Task StartAsync(ManagerWindow managerWindow)
    {
        manager = managerWindow;
        IsBusy = true;
        restoring = true;
        try
        {
            WidgetCatalog? saved = await storage.LoadCatalogAsync(lifetimeToken);
            if (saved is null)
            {
                var legacy = new WidgetCatalogEntry(Guid.NewGuid(), true);
                saved = new WidgetCatalog(1, [legacy], legacy.Id);
                if (!await storage.SaveCatalogAsync(saved, lifetimeToken))
                {
                    throw new IOException("widgets.catalog-save-failed");
                }
            }

            foreach (WidgetCatalogEntry entry in saved.Widgets)
            {
                await OpenAsync(entry);
            }

            lifetimeToken.ThrowIfCancellationRequested();
            initialized = true;
            SelectedWidget = Widgets.FirstOrDefault(widget => widget.Entry.Id == saved.SelectedWidgetId) ?? Widgets[0];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Do not overwrite an unreadable catalog with defaults or discard its widget identities.
            storage.Log("widgets.load-failed", exception.GetType().Name);
            Status = UiText.Get("WidgetsLoadFailed");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            restoring = false;
            IsBusy = false;
            DeleteWidgetCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task<WidgetInstance> OpenAsync(WidgetCatalogEntry entry)
    {
        WidgetSettingsStore widgetStorage = entry.UsesLegacySettings ? storage : storage.ForWidget(entry.Id);
        var viewModel = new WidgetViewModel();
        var controller = new WidgetController(host, widgetStorage, viewModel, monitoring, gestures);
        var instance = new WidgetInstance(entry, viewModel, controller);
        try
        {
            await monitoring.RegisterAsync(viewModel, widgetStorage, lifetimeToken);
            Widgets.Add(instance);
            await controller.StartAsync(manager!, demoMode);
            return instance;
        }
        catch
        {
            Widgets.Remove(instance);
            controller.Dispose();
            monitoring.Unregister(viewModel);
            throw;
        }
    }

    /// <summary>Create and persist a widget with its own source and an offset initial position.</summary>
    public async Task<WidgetInstance> CreateAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!initialized)
        {
            throw new InvalidOperationException("widgets.not-started");
        }

        await changes.WaitAsync(lifetimeToken);
        IsBusy = true;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (manager is null)
            {
                throw new InvalidOperationException("widgets.not-started");
            }

            var entry = new WidgetCatalogEntry(Guid.NewGuid());
            WidgetSettingsStore widgetStorage = storage.ForWidget(entry.Id);
            WidgetLayout anchor = SelectedWidget?.Controller.CurrentLayout ?? new WidgetLayout();
            WidgetLayout initialLayout = anchor with { X = anchor.X + 40, Y = anchor.Y + 40, Locked = true };
            string title = string.Format(UiText.Get("WidgetNumber"), Widgets.Count + 1);
            if (!await widgetStorage.SaveAsync(initialLayout, lifetimeToken) ||
                !await widgetStorage.SaveAppearanceAsync(new WidgetAppearance(title), lifetimeToken))
            {
                throw new IOException("widgets.settings-save-failed");
            }

            var entries = Widgets.Select(widget => widget.Entry).Append(entry).ToList();
            if (!await storage.SaveCatalogAsync(new WidgetCatalog(1, entries, entry.Id), lifetimeToken))
            {
                throw new IOException("widgets.catalog-save-failed");
            }

            WidgetInstance created = await OpenAsync(entry);
            SelectedWidget = created;
            Status = UiText.Get("WidgetCreated");
            return created;
        }
        finally
        {
            IsBusy = false;
            changes.Release();
            DeleteWidgetCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Remove one window after catalog persistence; keep NAS profiles and pollers intact.</summary>
    public async Task DeleteAsync(WidgetInstance instance)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await changes.WaitAsync(lifetimeToken);
        IsBusy = true;
        try
        {
            if (Widgets.Count <= 1 || !Widgets.Contains(instance))
            {
                return;
            }

            WidgetInstance next = ReferenceEquals(SelectedWidget, instance)
                ? Widgets.First(widget => !ReferenceEquals(widget, instance))
                : SelectedWidget ?? Widgets.First(widget => !ReferenceEquals(widget, instance));
            var entries = Widgets.Where(widget => !ReferenceEquals(widget, instance))
                .Select(widget => widget.Entry).ToList();
            if (!await storage.SaveCatalogAsync(new WidgetCatalog(1, entries, next.Entry.Id), lifetimeToken))
            {
                throw new IOException("widgets.catalog-save-failed");
            }

            // Stop native callbacks before removing subscriptions or changing the editor target.
            instance.Controller.Dispose();
            monitoring.Unregister(instance.ViewModel);
            Widgets.Remove(instance);
            SelectedWidget = next;
            Status = UiText.Get("WidgetDeleted");
        }
        finally
        {
            IsBusy = false;
            changes.Release();
            DeleteWidgetCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task SaveSelectionAsync()
    {
        try
        {
            await changes.WaitAsync(lifetimeToken);
            try
            {
                if (disposed || Widgets.Count == 0)
                {
                    return;
                }

                var catalog = new WidgetCatalog(1,
                    Widgets.Select(widget => widget.Entry).ToList(),
                    SelectedWidget?.Entry.Id);
                if (!await storage.SaveCatalogAsync(catalog, lifetimeToken))
                {
                    Status = UiText.Get("LayoutSaveFailed");
                }
            }
            finally
            {
                changes.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task CreateWidgetAsync()
    {
        try
        {
            await CreateAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = UiText.Get("LayoutSaveFailed");
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteWidgetAsync()
    {
        if (SelectedWidget is not { } selected)
        {
            return;
        }

        try
        {
            await DeleteAsync(selected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = UiText.Get("LayoutSaveFailed");
        }
        catch (OperationCanceledException)
        {
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using NasMonitor.Core;
using NasMonitor.Hosting;

namespace NasMonitor.App;

/// <summary>Owns one shared synthetic stream and the native widget lifecycle.</summary>
public sealed class WidgetController : IDisposable
{
    private readonly IDesktopWidgetHost host;
    private readonly SpikeStorage storage;
    private readonly SpikeViewModel viewModel;
    private readonly WidgetMonitoringPresenter monitoring;
    private bool demoMode;
    private bool contentMinimumHeightVerified;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private WidgetLayout layout = new();
    private WidgetWindow? widget;
    private ManagerWindow? manager;
    private NativeWidget? nativeWidget;
    private long tick;
    private bool disposed;
    private string lastHostCode = "";
    private double lastAppliedScale;
    private WidgetDragSession? dragSession;
    private WidgetResizeSession? resizeSession;
    private bool HasPointerGesture => dragSession is not null || resizeSession is not null;
    private readonly DispatcherTimer pointerTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool pointerWasPressed;

    internal bool IsSmokeHealthy => tick > 0 && IsHostAlive() && widget?.IsVisible == true && widget.Opacity > 0;

    /// <summary>Machine-readable smoke evidence; API success is not visual acceptance.</summary>
    public object GetSmokeReport()
    {
        return new
        {
            OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            SyntheticUpdates = tick,
            ContentMinimumHeightVerified = contentMinimumHeightVerified,
            NativeHostCode = lastHostCode,
            NativeHostAlive = IsHostAlive(),
            WidgetVisible = widget?.IsVisible == true,
            VisualDesktopAcceptance = "Unverified"
        };
    }

    /// <summary>Inject the platform host, persistence, and platform-independent state.</summary>
    public WidgetController(IDesktopWidgetHost host, SpikeStorage storage, SpikeViewModel viewModel,
        WidgetMonitoringPresenter monitoring)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(viewModel);
        this.host = host;
        this.storage = storage;
        this.viewModel = viewModel;
        this.monitoring = monitoring;
        timer.Tick += OnTick;
        pointerTimer.Tick += OnPointerTick;
    }

    /// <summary>Load geometry and start monitoring only after Manager has a screen.</summary>
    public async Task StartAsync(ManagerWindow managerWindow, bool useDemoMetrics = false)
    {
        demoMode = useDemoMetrics;
        manager = managerWindow;
        try
        {
            layout = await storage.LoadAsync(lifetime.Token);
            WidgetAppearance appearance = await storage.LoadAppearanceAsync(lifetime.Token);
            viewModel.ApplyAppearance(appearance);
            manager.DisplayAppearance(appearance);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            return;
        }
        if (disposed)
        {
            return;
        }
        layout = FitLayout(layout);
        manager.DisplayLayout(layout);
        if (demoMode)
        {
            viewModel.Update(DemoSnapshot.Create(tick, DateTimeOffset.UtcNow));
        }
        else
        {
            monitoring.Refresh();
        }
        Restore();
        timer.Start();
    }

    /// <summary>Apply validated layout and persist without blocking the UI.</summary>
    public async Task ApplyAsync(WidgetLayout requestedLayout)
    {
        if (disposed)
        {
            return;
        }
        widget?.EndDrag();
        layout = FitLayout(requestedLayout);
        manager?.DisplayLayout(layout);
        Restore();
        await SaveLayoutAsync(layout);
    }

    private async Task SaveLayoutAsync(WidgetLayout savedLayout)
    {
        try
        {
            bool saved = await storage.SaveAsync(savedLayout, lifetime.Token);
            viewModel.StorageStatus = UiText.Get(saved ? "LayoutSaved" : "LayoutSaveFailed");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Shutdown cancels outstanding persistence without an async-void failure.
        }
    }

    internal async Task ApplyAppearanceAsync(WidgetAppearance requested)
    {
        if (disposed)
        {
            return;
        }
        WidgetAppearance appearance = requested.Normalize();
        viewModel.ApplyAppearance(appearance);
        manager?.DisplayAppearance(appearance);
        Restore();
        try
        {
            bool saved = await storage.SaveAppearanceAsync(appearance, lifetime.Token);
            viewModel.StorageStatus = UiText.Get(saved ? "AppearanceSaved" : "LayoutSaveFailed");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    internal bool BeginDrag(PixelPoint pointer, bool fromDesktopHost = false)
    {
        if (host is IDesktopPointerSource && !fromDesktopHost)
        {
            return false;
        }
        if (disposed || layout.Locked || HasPointerGesture || !IsHostAlive())
        {
            return false;
        }
        double scale = CurrentScale();
        if (WidgetResizeSession.IsOverGrip(layout, pointer.X, pointer.Y, scale))
        {
            resizeSession = new WidgetResizeSession(layout, pointer.X, pointer.Y, scale);
        }
        else
        {
            dragSession = new WidgetDragSession(layout, pointer.X, pointer.Y);
        }
        return true;
    }

    internal void MoveDrag(PixelPoint pointer)
    {
        if (disposed || layout.Locked || !HasPointerGesture || widget is null || nativeWidget is null)
        {
            return;
        }
        DisplayArea[] displays = GetDisplays();
        WidgetLayout requested = resizeSession is not null
            ? resizeSession.ResizeTo(pointer.X, pointer.Y, displays)
            : dragSession!.MoveTo(pointer.X, pointer.Y, displays);
        requested = FitContentHeight(requested);
        if (requested == layout)
        {
            return;
        }
        try
        {
            widget.Width = requested.Width;
            widget.Height = requested.Height;
            // Avalonia owns top-level positioning on AppKit. WorkerW child coordinates
            // must instead be converted by the Windows host, without a top-level move.
            if (nativeWidget.Descriptor != "HWND")
            {
                widget.Position = new PixelPoint(requested.X, requested.Y);
            }
            double scale = ScaleAt(requested);
            var bounds = new NativeBounds(requested.X, requested.Y,
                (int)Math.Round(requested.Width * scale), (int)Math.Round(requested.Height * scale));
            DesktopAttachResult result = host.Attach(nativeWidget, bounds, false, lifetime.Token);
            SetHostStatus(result);
            if (!result.Success)
            {
                widget.EndDrag();
                EndDrag();
                widget.Hide();
                return;
            }
            layout = requested;
            lastAppliedScale = scale;
            manager?.DisplayLayout(layout);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            storage.Log("host.drag-error", exception.GetType().Name);
            SetHostStatus(new(false, "host.drag-error"));
            widget.EndDrag();
            EndDrag();
            widget.Hide();
        }
    }

    internal void EndDrag()
    {
        if (!HasPointerGesture)
        {
            return;
        }
        dragSession = null;
        resizeSession = null;
        if (!disposed)
        {
            _ = SaveLayoutAsync(layout);
        }
    }

    internal Task SetDragModeAsync(bool enabled)
    {
        return ApplyAsync(layout with { Locked = !enabled });
    }

    internal async Task VerifyContentMinimumHeightAsync()
    {
        WidgetAppearance originalAppearance = viewModel.CurrentAppearance;
        WidgetLayout originalLayout = layout;
        try
        {
            layout = layout with { Height = 72 };
            viewModel.ApplyAppearance(new WidgetAppearance());
            Restore();
            await Task.Delay(200);
            double expandedHeight = widget?.Height ?? 0;
            double enlargedHeight = Math.Min(600, expandedHeight + 80);
            layout = layout with { Height = enlargedHeight };
            Restore();
            await Task.Delay(200);
            if (Math.Abs((widget?.Height ?? 0) - enlargedHeight) > 1)
            {
                throw new InvalidOperationException("widget.manual-height-not-preserved");
            }
            layout = layout with { Height = 72 };
            Restore();
            await Task.Delay(200);
            if (Math.Abs((widget?.Height ?? 0) - expandedHeight) > 1)
            {
                throw new InvalidOperationException("widget.content-minimum-not-enforced");
            }
            viewModel.ApplyAppearance(new WidgetAppearance(ShowCpu: false, ShowRam: false, ShowLastUpdate: false,
                ShowStorage: false, ShowNetwork: false, ShowTemperature: false, ShowUptime: false));
            layout = layout with { Height = 72 };
            Restore();
            await Task.Delay(200);
            double compactHeight = widget?.Height ?? 0;
            if (expandedHeight < compactHeight + 100 || compactHeight > 150 || compactHeight < 72)
            {
                throw new InvalidOperationException("widget.flexible-height-failed");
            }
            contentMinimumHeightVerified = true;
        }
        finally
        {
            viewModel.ApplyAppearance(originalAppearance);
            layout = originalLayout;
            Restore();
        }
    }

    private void OnPointerTick(object? sender, EventArgs args)
    {
        if (disposed || layout.Locked || nativeWidget is null || widget?.IsVisible != true ||
            host is not IDesktopPointerSource source)
        {
            return;
        }
        try
        {
            if (!source.TryReadPointer(out DesktopPointer pointer))
            {
                pointerWasPressed = true;
                EndDrag();
                return;
            }
            bool newPress = pointer.LeftButtonPressed && !pointerWasPressed;
            pointerWasPressed = pointer.LeftButtonPressed;
            var position = new PixelPoint(pointer.X, pointer.Y);
            if (HasPointerGesture)
            {
                MoveDrag(position);
                if (!pointer.LeftButtonPressed)
                {
                    EndDrag();
                }
                return;
            }
            double scale = CurrentScale();
            bool inside = pointer.X >= layout.X && pointer.Y >= layout.Y &&
                pointer.X < layout.X + layout.Width * scale && pointer.Y < layout.Y + layout.Height * scale;
            if (newPress && inside && source.CanStartDrag(nativeWidget, pointer))
            {
                BeginDrag(position, fromDesktopHost: true);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            storage.Log("host.pointer-error", exception.GetType().Name);
            EndDrag();
            pointerTimer.Stop();
            viewModel.StorageStatus = UiText.Get("PointerFailed");
        }
    }

    /// <summary>Attempt desktop attachment. Failure hides the widget, leaving Manager usable.</summary>
    public void Restore()
    {
        if (disposed || manager is null)
        {
            return;
        }
        try
        {
            widget?.EndDrag();
            EndDrag();
            pointerTimer.Stop();
            if (widget is null || nativeWidget is null || !IsHostAlive())
            {
                DestroyWidget();
                widget = new WidgetWindow { DataContext = viewModel };
                widget.SetController(this);
                widget.Width = layout.Width;
                widget.Height = layout.Height;
                widget.Position = new PixelPoint(layout.X, layout.Y);
                // Invisible until native placement succeeds: never present a floating fallback.
                widget.Show();
                var handle = widget.TryGetPlatformHandle();
                if (handle is null)
                {
                    SetHostStatus(new(false, "native-handle-unavailable"));
                    widget.Hide();
                    return;
                }
                nativeWidget = new NativeWidget(handle.Handle, handle.HandleDescriptor ?? "unknown");
            }

            widget.Opacity = 0;
            if (!widget.IsVisible)
            {
                widget.Show();
            }
            widget.Width = layout.Width;
            layout = FitContentHeight(layout);
            widget.Height = layout.Height;
            widget.SetLocked(layout.Locked);
            viewModel.IsEditing = !layout.Locked;
            widget.Position = new PixelPoint(layout.X, layout.Y);
            double scale = CurrentScale();
            var bounds = new NativeBounds(layout.X, layout.Y, (int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
            DesktopAttachResult result = host.Attach(nativeWidget, bounds, layout.Locked, lifetime.Token);
            manager.DisplayLayout(layout);
            SetHostStatus(result);
            if (result.Success)
            {
                widget.Opacity = 1;
                lastAppliedScale = scale;
                if (!layout.Locked && host is IDesktopPointerSource source)
                {
                    pointerWasPressed = !source.TryReadPointer(out DesktopPointer pointer) || pointer.LeftButtonPressed;
                    pointerTimer.Start();
                }
            }
            else
            {
                widget.Hide();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Native host refusal must not take down the diagnostic Manager.
            if (widget is not null)
            {
                widget.Opacity = 0;
            }
            storage.Log("host.exception", exception.GetType().Name);
            SetHostStatus(new(false, "host.exception-" + exception.GetType().Name));
        }
    }

    private WidgetLayout FitLayout(WidgetLayout requested)
    {
        return FitContentHeight(requested.Fit(GetDisplays()));
    }

    private WidgetLayout FitContentHeight(WidgetLayout requested)
    {
        if (widget is null)
        {
            return requested;
        }
        double height = widget.MeasureContentHeight(requested.Width);
        viewModel.MinimumWidgetHeight = (decimal)Math.Clamp(height, 72, 600);
        return requested.WithContentMinimum(height).Fit(GetDisplays());
    }

    private DisplayArea[] GetDisplays()
    {
        return manager?.Screens.All.OrderByDescending(screen => screen.IsPrimary)
            .Select(screen => new DisplayArea(screen.WorkingArea.X, screen.WorkingArea.Y,
                screen.WorkingArea.Width, screen.WorkingArea.Height, screen.Scaling)).ToArray() ?? [];
    }

    private void OnTick(object? sender, EventArgs args)
    {
        tick++;
        if (demoMode)
        {
            viewModel.Update(DemoSnapshot.Create(tick, DateTimeOffset.UtcNow));
        }
        else
        {
            monitoring.Refresh();
        }
        WidgetLayout fitted = FitLayout(layout);
        bool changed = fitted != layout;
        layout = fitted;
        if (changed)
        {
            manager?.DisplayLayout(layout);
        }
        // Healthy windows are not hidden/repositioned on each metrics tick.
        // A native-state failure, display change, or an earlier refusal triggers recovery.
        if (changed || CurrentScale() != lastAppliedScale || !IsHostAlive() || widget?.IsVisible != true)
        {
            Restore();
        }
    }

    private double CurrentScale()
    {
        return ScaleAt(layout);
    }

    private double ScaleAt(WidgetLayout placement)
    {
        var position = new PixelPoint(placement.X, placement.Y);
        return manager?.Screens.All.FirstOrDefault(screen => screen.Bounds.Contains(position))?.Scaling ?? 1;
    }

    private bool IsHostAlive()
    {
        if (nativeWidget is null)
        {
            return false;
        }
        try
        {
            return host.IsAlive(nativeWidget);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            storage.Log("host.health-error", exception.GetType().Name);
            return false;
        }
    }

    private void SetHostStatus(DesktopAttachResult result)
    {
        viewModel.HostStatus = UiText.Get(result.Success ? "HostApplied" : "HostFailed") + result.Code;
        if (lastHostCode != result.Code)
        {
            storage.Log("host.status", result.Code);
            lastHostCode = result.Code;
        }
    }

    private void DestroyWidget()
    {
        widget?.EndDrag();
        if (nativeWidget is not null)
        {
            host.Detach(nativeWidget);
        }
        nativeWidget = null;
        widget?.Close();
        widget = null;
    }

    /// <summary>Stop ticks and release host ownership before destroying native windows.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        timer.Stop();
        timer.Tick -= OnTick;
        pointerTimer.Stop();
        pointerTimer.Tick -= OnPointerTick;
        lifetime.Cancel();
        try
        {
            DestroyWidget();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            storage.Log("shutdown.host-error", exception.GetType().Name);
        }
        lifetime.Dispose();
    }
}

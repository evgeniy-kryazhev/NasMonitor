using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NasMonitor.App.Services;

namespace NasMonitor.App.Views.Windows;

/// <summary>One common Avalonia view for both desktop hosts.</summary>
public partial class WidgetWindow : Window
{
    private WidgetController? controller;
    private IPointer? dragPointer;

    /// <summary>Create the initially transparent widget.</summary>
    public WidgetWindow()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, PointerPressedOnWidget, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, PointerMovedOnWidget, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, PointerReleasedOnWidget, RoutingStrategies.Tunnel);
        PointerCaptureLost += CaptureLost;
    }

    internal void SetController(WidgetController widgetController)
    {
        controller = widgetController;
    }

    internal double MeasureContentHeight(double width)
    {
        return WidgetContent.MeasureContentHeight(width);
    }

    internal void SetLocked(bool locked)
    {
        if (locked)
        {
            EndDrag();
        }

        Cursor = locked ? Cursor.Default : new Cursor(StandardCursorType.SizeAll);
    }

    private void PointerPressedOnWidget(object? sender, PointerPressedEventArgs args)
    {
        if (dragPointer is not null || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        PixelPoint position = this.PointToScreen(args.GetPosition(this));
        if (controller?.BeginDrag(position) != true)
        {
            return;
        }

        dragPointer = args.Pointer;
        args.Pointer.Capture(this);
        args.Handled = true;
    }

    private void PointerMovedOnWidget(object? sender, PointerEventArgs args)
    {
        if (dragPointer != args.Pointer)
        {
            return;
        }

        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }

        controller?.MoveDrag(this.PointToScreen(args.GetPosition(this)));
        args.Handled = true;
    }

    private void PointerReleasedOnWidget(object? sender, PointerReleasedEventArgs args)
    {
        if (dragPointer != args.Pointer || args.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        controller?.MoveDrag(this.PointToScreen(args.GetPosition(this)));
        EndDrag();
        args.Handled = true;
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        EndDrag();
    }

    internal void EndDrag()
    {
        IPointer? pointer = dragPointer;
        dragPointer = null;
        if (pointer is null)
        {
            return;
        }

        if (ReferenceEquals(pointer.Captured, this))
        {
            pointer.Capture(null);
        }

        controller?.EndDrag();
    }
}

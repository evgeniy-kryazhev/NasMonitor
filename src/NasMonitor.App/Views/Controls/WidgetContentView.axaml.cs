using Avalonia;
using Avalonia.Controls;

namespace NasMonitor.App.Views.Controls;

/// <summary>Shared metric presentation used by the real widget and the settings preview.</summary>
public partial class WidgetContentView : UserControl
{
    /// <summary>Create the common presentation without native window behavior.</summary>
    public WidgetContentView()
    {
        InitializeComponent();
    }

    internal double MeasureContentHeight(double width)
    {
        // Match the surface's 16-DIP padding and 1-DIP border on each side.
        MetricStack.Measure(new Size(Math.Max(1, width - 34), double.PositiveInfinity));
        return Math.Ceiling(MetricStack.DesiredSize.Height + 34);
    }
}

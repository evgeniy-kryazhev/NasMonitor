using NasMonitor.App.Persistence;
using NasMonitor.App.Services;
using NasMonitor.App.ViewModels;
using NasMonitor.Core.Widgets;

namespace NasMonitor.App.Models;

/// <summary>Independent runtime and editor state for one persistent desktop widget.</summary>
public sealed class WidgetInstance(
    WidgetCatalogEntry entry,
    WidgetViewModel viewModel,
    WidgetController controller)
{
    /// <summary>Stable catalog identity, independent of the selected NAS.</summary>
    public WidgetCatalogEntry Entry
    {
        get;
    } = entry;

    /// <summary>Appearance and metrics consumed only by this window and its editor.</summary>
    public WidgetViewModel ViewModel
    {
        get;
    } = viewModel;

    /// <summary>Owner of the corresponding native window.</summary>
    public WidgetController Controller
    {
        get;
    } = controller;

    /// <summary>Unapplied editor values retained while editing another widget.</summary>
    public WidgetAppearance? AppearanceDraft
    {
        get;
        set;
    }
}

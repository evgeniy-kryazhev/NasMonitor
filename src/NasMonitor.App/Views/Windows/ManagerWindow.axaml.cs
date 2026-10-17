using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using NasMonitor.App.Models;
using NasMonitor.App.Services;
using NasMonitor.App.ViewModels;
using NasMonitor.App.Views.Pages;
using NasMonitor.Core.Profiles;
using NasMonitor.Core.Widgets;

namespace NasMonitor.App.Views.Windows;

/// <summary>Main shell with persistent widget and connection pages.</summary>
public partial class ManagerWindow : Window
{
    private readonly NasConnectionsView connections = new();
    private readonly NasConnectionsViewModel? connectionsViewModel;
    private readonly WidgetSettingsView widgetSettings = new();
    private readonly WidgetWorkspace? workspace;
    private WidgetInstance? editedWidget;

    /// <summary>Create the navigation shell.</summary>
    public ManagerWindow()
    {
        InitializeComponent();
        PageHost.Content = widgetSettings;
    }

    /// <summary>Supply the independent page view models and widget controller.</summary>
    public ManagerWindow(WidgetWorkspace workspace,
        NasConnectionsViewModel connectionsViewModel) : this()
    {
        this.connectionsViewModel = connectionsViewModel;
        this.workspace = workspace;
        widgetSettings.InitializeWorkspace(workspace);
        workspace.SelectionChanged += SelectWidget;
        Closed += (_, _) => workspace.SelectionChanged -= SelectWidget;
        connections.DataContext = connectionsViewModel;
        SidebarNas.DataContext = connectionsViewModel;
        Opened += async (_, _) => await connectionsViewModel.InitializeAsync();
        Closed += (_, _) => connectionsViewModel.Dispose();
        Closed += (_, _) => widgetSettings.Dispose();
    }

    private WidgetController? widgetController => editedWidget?.Controller;

    private void SelectWidget(WidgetInstance? instance)
    {
        if (editedWidget is not null)
        {
            editedWidget.AppearanceDraft = widgetSettings.CaptureAppearanceDraft();
        }

        editedWidget = instance;
        if (instance is null)
        {
            widgetSettings.ClearWidget();
            return;
        }

        widgetSettings.Initialize(instance.Controller, instance.ViewModel);
        widgetSettings.DisplayLayout(instance.Controller.CurrentLayout);
        widgetSettings.DisplayAppearance(instance.AppearanceDraft ?? instance.ViewModel.CurrentAppearance);
    }

    internal void DisplayLayout(WidgetController controller, WidgetLayout layout)
    {
        if (ReferenceEquals(editedWidget?.Controller, controller))
        {
            widgetSettings.DisplayLayout(layout);
        }
    }

    internal void DisplayAppearance(WidgetController controller, WidgetAppearance appearance)
    {
        WidgetInstance? instance =
            workspace?.Widgets.FirstOrDefault(item => ReferenceEquals(item.Controller, controller));
        if (instance is not null)
        {
            instance.AppearanceDraft = null;
        }

        if (ReferenceEquals(editedWidget?.Controller, controller))
        {
            widgetSettings.DisplayAppearance(appearance);
        }
    }

    internal void OpenConnections()
    {
        Navigation.SelectedIndex = 1;
    }

    private void SidebarNasChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (SidebarNas.SelectedItem is NasProfile && !ReferenceEquals(PageHost.Content, connections))
        {
            OpenConnections();
        }
    }

    private void SidebarNasTapped(object? sender, TappedEventArgs args)
    {
        OpenSelectedNas();
    }

    private void OpenSelectedNas()
    {
        if (SidebarNas.SelectedItem is NasProfile)
        {
            OpenConnections();
        }
    }

    private void SidebarNasKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && SidebarNas.SelectedItem is NasProfile)
        {
            OpenConnections();
            args.Handled = true;
        }
    }

    private void NavigationChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (PageHost is null || sender is not ListBox navigation)
        {
            return;
        }

        if (navigation.SelectedIndex == 1)
        {
            PageHost.Content = connections;
        }
        else
        {
            connectionsViewModel?.LeavePage();
            PageHost.Content = widgetSettings;
        }
    }

    private void ThemeChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (Application.Current is { } app && sender is ComboBox combo)
        {
            app.RequestedThemeVariant = combo.SelectedIndex switch
            {
                1 => ThemeVariant.Light,
                2 => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
    }
}

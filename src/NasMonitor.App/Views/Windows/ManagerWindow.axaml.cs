using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
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
    private readonly WidgetController? widgetController;
    private readonly WidgetSettingsView widgetSettings = new();

    /// <summary>Create the navigation shell.</summary>
    public ManagerWindow()
    {
        InitializeComponent();
        PageHost.Content = widgetSettings;
    }

    /// <summary>Supply the independent page view models and widget controller.</summary>
    public ManagerWindow(WidgetController controller,
        WidgetViewModel viewModel,
        NasConnectionsViewModel connectionsViewModel) : this()
    {
        this.connectionsViewModel = connectionsViewModel;
        widgetController = controller;
        widgetSettings.Initialize(controller, viewModel);
        connections.DataContext = connectionsViewModel;
        SidebarNas.DataContext = connectionsViewModel;
        Opened += async (_, _) => await connectionsViewModel.InitializeAsync();
        Closed += (_, _) => connectionsViewModel.Dispose();
        Closed += (_, _) => widgetSettings.Dispose();
    }

    internal void DisplayLayout(WidgetLayout layout)
    {
        widgetSettings.DisplayLayout(layout);
    }

    internal void DisplayAppearance(WidgetAppearance appearance)
    {
        widgetSettings.DisplayAppearance(appearance);
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

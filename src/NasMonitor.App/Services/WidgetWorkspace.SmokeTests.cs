using NasMonitor.App.Models;
using NasMonitor.App.Persistence;
using NasMonitor.Core.Profiles;
using NasMonitor.Core.Widgets;

namespace NasMonitor.App.Services;

public sealed partial class WidgetWorkspace
{
    internal bool MultipleWidgetsVerified
    {
        get;
        private set;
    }

    internal async Task VerifyMultipleWidgetsAsync(Func<Task> verifyEditor)
    {
        if (!demoMode || Widgets.Count == 0)
        {
            throw new InvalidOperationException("widgets.smoke-mode-required");
        }

        WidgetInstance first = Widgets[0];
        if (Widgets.Count == 1)
        {
            await DeleteAsync(first);
            if (Widgets.Count != 1 || !Widgets.Contains(first))
            {
                throw new InvalidOperationException("widgets.last-widget-deleted");
            }
        }

        WidgetInstance second = Widgets.Count > 1 ? Widgets[1] : await CreateAsync();
        var firstNas = new NasProfile(Guid.NewGuid(),
            "Home NAS · TEST",
            new Uri("https://home.example:5001"),
            "fixture");
        var secondNas = new NasProfile(Guid.NewGuid(),
            "Backup NAS · TEST",
            new Uri("https://backup.example:5001"),
            "fixture");
        await monitoring.SynchronizeAsync([firstNas, secondNas], lifetimeToken);
        first.ViewModel.SelectedNas = firstNas;
        second.ViewModel.SelectedNas = secondNas;
        await monitoring.FlushAsync(first.ViewModel, lifetimeToken);
        await monitoring.FlushAsync(second.ViewModel, lifetimeToken);

        WidgetLayout originalLayout = first.Controller.CurrentLayout;
        WidgetAppearance originalAppearance = first.ViewModel.CurrentAppearance;
        await second.Controller.ApplyAppearanceAsync(new WidgetAppearance("Backup NAS", "#799CE8"));
        await second.Controller.ApplyAsync(originalLayout with
        {
            X = originalLayout.X + 80, Y = originalLayout.Y + 80
        });
        SelectedWidget = first;
        await verifyEditor();
        if (first.ViewModel.SelectedNas?.Id != firstNas.Id || second.ViewModel.SelectedNas?.Id != secondNas.Id ||
            first.Controller.CurrentLayout != originalLayout || first.ViewModel.CurrentAppearance != originalAppearance)
        {
            throw new InvalidOperationException("widgets.state-isolation-failed");
        }

        await second.Controller.VerifyContentMinimumHeightAsync();
        WidgetInstance temporary = await CreateAsync();
        int countBeforeDelete = Widgets.Count;
        await DeleteAsync(temporary);
        if (Widgets.Count != countBeforeDelete - 1 || temporary.Controller.IsSmokeHealthy ||
            !Widgets.Contains(first) || !Widgets.Contains(second))
        {
            throw new InvalidOperationException("widgets.delete-isolation-failed");
        }

        SelectedWidget = first;
        await SaveSelectionAsync();
        WidgetCatalog? saved = await storage.LoadCatalogAsync(lifetimeToken);
        if (saved?.Widgets.Count != Widgets.Count || saved.SelectedWidgetId != first.Entry.Id ||
            !saved.Widgets.Select(entry => entry.Id).SequenceEqual(Widgets.Select(widget => widget.Entry.Id)))
        {
            throw new InvalidOperationException("widgets.catalog-roundtrip-failed");
        }

        await Task.Delay(2200, lifetimeToken);
        if (!first.Controller.IsSmokeHealthy || !second.Controller.IsSmokeHealthy)
        {
            throw new InvalidOperationException("widgets.multiple-native-hosts-failed");
        }

        MultipleWidgetsVerified = true;
    }
}

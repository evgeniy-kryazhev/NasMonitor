using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NasMonitor.App.Composition;
using NasMonitor.App.Persistence;
using NasMonitor.App.Services;
using NasMonitor.App.ViewModels;
using NasMonitor.App.Views.Windows;

namespace NasMonitor.App;

/// <summary>Composition root for desktop widgets and DSM connection services.</summary>
public class App : Application
{
    private ServiceProvider? services;

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            bool smokeMode = desktop.Args?.Contains("--smoke-test") == true;
            services = ApplicationServices.Create(smokeMode);
            WidgetWorkspace workspace = services.GetRequiredService<WidgetWorkspace>();
            var manager = new ManagerWindow(workspace,
                services.GetRequiredService<NasConnectionsViewModel>());
            desktop.MainWindow = manager;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            manager.Opened += async (_, _) =>
            {
                await workspace.StartAsync(manager);
                int restoredWidgetCount = workspace.Widgets.Count;
                if (smokeMode)
                {
                    // Capture both real pages under both themes without contacting a NAS.
                    try
                    {
                        await manager.CaptureSmokePagesAsync();
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        Directory.CreateDirectory("artifacts");
                        File.WriteAllText("artifacts/smoke-ui-failure.txt",
                            exception.GetType().Name + Environment.NewLine + exception.StackTrace);
                        services.GetRequiredService<WidgetSettingsStore>()
                            .Log("smoke.ui-failed", exception.GetType().Name);
                        desktop.Shutdown(1);
                        return;
                    }

                    DispatcherTimer.RunOnce(() =>
                        {
                            int exitCode = 1;
                            try
                            {
                                Directory.CreateDirectory("artifacts");
                                File.WriteAllText("artifacts/smoke-report.json",
                                    JsonSerializer.Serialize(new
                                        {
                                            WidgetCount = workspace.Widgets.Count,
                                            RestoredWidgetCount = restoredWidgetCount,
                                            workspace.MultipleWidgetsVerified,
                                            Widgets = workspace.Widgets
                                                .Select(item => item.Controller.GetSmokeReport()).ToArray()
                                        },
                                        new JsonSerializerOptions { WriteIndented = true }));
                                exitCode = workspace.Widgets.Count > 0 &&
                                           workspace.Widgets.All(item => item.Controller.IsSmokeHealthy)
                                    ? 0
                                    : 1;
                            }
                            finally
                            {
                                desktop.Shutdown(exitCode);
                            }
                        },
                        TimeSpan.FromSeconds(8));
                }
            };
            desktop.Exit += (_, _) =>
            {
                workspace.Dispose();
                services.GetRequiredService<WidgetMonitoringPresenter>().Dispose();
                services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

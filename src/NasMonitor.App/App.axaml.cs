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
            WidgetController controller = services.GetRequiredService<WidgetController>();
            var manager = new ManagerWindow(controller,
                services.GetRequiredService<WidgetViewModel>(),
                services.GetRequiredService<NasConnectionsViewModel>());
            desktop.MainWindow = manager;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            manager.Opened += async (_, _) =>
            {
                await controller.StartAsync(manager, smokeMode);
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
                                    JsonSerializer.Serialize(controller.GetSmokeReport(),
                                        new JsonSerializerOptions { WriteIndented = true }));
                                exitCode = controller.IsSmokeHealthy ? 0 : 1;
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
                controller.Dispose();
                services.GetRequiredService<WidgetMonitoringPresenter>().Dispose();
                services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

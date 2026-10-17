using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NasMonitor.Hosting;
using NasMonitor.Platform.MacOS;
using NasMonitor.Platform.Windows;
using Avalonia.Threading;
using System.Text.Json;
using NasMonitor.Core;
using NasMonitor.Infrastructure;
using NasMonitor.Providers.Synology;

namespace NasMonitor.App;

/// <summary>Composition root for desktop widgets and DSM connection services.</summary>
public partial class App : Avalonia.Application
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
            var registrations = new ServiceCollection();
            registrations.AddSingleton<SpikeStorage>();
            registrations.AddSingleton<SpikeViewModel>();
            registrations.AddSingleton<ISecretStore>(_ => OperatingSystem.IsWindows()
                ? new WindowsSecretStore()
                : OperatingSystem.IsMacOS() ? new MacSecretStore() : new UnsupportedSecretStore());
            registrations.AddSingleton(_ => new NasProfileRepository(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NasMonitor", "profiles.json")));
            registrations.AddSingleton<NasProfileService>();
            registrations.AddSingleton(TimeProvider.System);
            registrations.AddHttpClient("DsmMetrics", client => client.Timeout = TimeSpan.FromSeconds(15))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                }).RemoveAllLoggers();
            registrations.AddSingleton<INasMetricsSessionFactory>(provider => new SynologyMetricsSessionFactory(
                () => provider.GetRequiredService<IHttpClientFactory>().CreateClient("DsmMetrics")));
            registrations.AddSingleton<NasMonitoringCoordinator>();
            registrations.AddSingleton(provider => new WidgetMonitoringPresenter(
                provider.GetRequiredService<NasMonitoringCoordinator>(), provider.GetRequiredService<SpikeStorage>(),
                provider.GetRequiredService<SpikeViewModel>(), desktop.Args?.Contains("--smoke-test") != true));
            registrations.AddSingleton<IDsmTransportProbe, DsmTransportProbe>();
            registrations.AddHttpClient<INasConnectionTester, SynologyConnectionTester>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            }).RemoveAllLoggers();
            registrations.AddTransient<NasConnectionsViewModel>();
            registrations.AddSingleton<IDesktopWidgetHost>(_ => OperatingSystem.IsWindows()
                ? new WindowsDesktopHost()
                : OperatingSystem.IsMacOS() ? new MacDesktopHost() : new UnsupportedHost());
            registrations.AddSingleton<WidgetController>();
            services = registrations.BuildServiceProvider();
            var controller = services.GetRequiredService<WidgetController>();
            var manager = new ManagerWindow(controller, services.GetRequiredService<SpikeViewModel>(),
                services.GetRequiredService<NasConnectionsViewModel>());
            desktop.MainWindow = manager;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            manager.Opened += async (_, _) =>
            {
                await controller.StartAsync(manager, desktop.Args?.Contains("--smoke-test") == true);
                if (desktop.Args?.Contains("--smoke-test") == true)
                {
                    // Capture both real pages under both themes without contacting a NAS.
                    try
                    {
                        await manager.CaptureSmokePagesAsync();
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        services.GetRequiredService<SpikeStorage>().Log("smoke.ui-failed", exception.GetType().Name);
                        desktop.Shutdown(1);
                        return;
                    }
                    DispatcherTimer.RunOnce(() =>
                    {
                        int exitCode = 1;
                        try
                        {
                            Directory.CreateDirectory("artifacts");
                            File.WriteAllText("artifacts/smoke-report.json", JsonSerializer.Serialize(controller.GetSmokeReport(), new JsonSerializerOptions { WriteIndented = true }));
                            exitCode = controller.IsSmokeHealthy ? 0 : 1;
                        }
                        finally
                        {
                            desktop.Shutdown(exitCode);
                        }
                    }, TimeSpan.FromSeconds(8));
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

    private sealed class UnsupportedHost : IDesktopWidgetHost
    {
        public DesktopAttachResult Attach(NativeWidget widget, NativeBounds bounds, bool locked, CancellationToken cancellationToken)
        {
            return new(false, "unsupported-os");
        }

        public bool IsAlive(NativeWidget widget)
        {
            return false;
        }

        public void Detach(NativeWidget widget)
        {
        }
    }

    private sealed class UnsupportedSecretStore : ISecretStore
    {
        public Task WriteAsync(Guid credentialId, string password, CancellationToken cancellationToken)
        {
            throw new PlatformNotSupportedException();
        }

        public Task<string?> ReadAsync(Guid credentialId, CancellationToken cancellationToken)
        {
            throw new PlatformNotSupportedException();
        }

        public Task DeleteAsync(Guid credentialId, CancellationToken cancellationToken)
        {
            throw new PlatformNotSupportedException();
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using NasMonitor.App.Persistence;
using NasMonitor.App.Services;
using NasMonitor.App.ViewModels;
using NasMonitor.Core.Connections;
using NasMonitor.Core.Monitoring;
using NasMonitor.Core.Security;
using NasMonitor.Hosting.Abstractions;
using NasMonitor.Hosting.Models;
using NasMonitor.Infrastructure.Monitoring;
using NasMonitor.Infrastructure.Persistence;
using NasMonitor.Infrastructure.Profiles;
using NasMonitor.Platform.MacOS.Desktop;
using NasMonitor.Platform.MacOS.Security;
using NasMonitor.Platform.Windows.Desktop;
using NasMonitor.Platform.Windows.Security;
using NasMonitor.Providers.Synology.Connections;
using NasMonitor.Providers.Synology.Monitoring;
using NasMonitor.Providers.Synology.Transport;

namespace NasMonitor.App.Composition;

/// <summary>Registers application services and chooses isolated platform implementations.</summary>
internal static class ApplicationServices
{
    internal static ServiceProvider Create(bool smokeMode)
    {
        var registrations = new ServiceCollection();
        registrations.AddSingleton(_ => smokeMode
            ? new WidgetSettingsStore(Path.GetFullPath("artifacts/smoke-settings"))
            : new WidgetSettingsStore());
        registrations.AddSingleton<ISecretStore>(_ => OperatingSystem.IsWindows()
            ? new WindowsSecretStore()
            : OperatingSystem.IsMacOS()
                ? new MacSecretStore()
                : new UnsupportedSecretStore());
        registrations.AddSingleton(_ => new NasProfileRepository(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NasMonitor",
            "profiles.json")));
        registrations.AddSingleton<NasProfileService>();
        registrations.AddSingleton(TimeProvider.System);
        registrations.AddHttpClient("DsmMetrics", client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false, UseCookies = false
            }).RemoveAllLoggers();
        registrations.AddSingleton<INasMetricsSessionFactory>(provider =>
            new SynologyMetricsSessionFactory(() =>
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("DsmMetrics")));
        registrations.AddSingleton<NasMonitoringCoordinator>();
        registrations.AddSingleton(provider => new WidgetMonitoringPresenter(
            provider.GetRequiredService<NasMonitoringCoordinator>(),
            !smokeMode));
        registrations.AddSingleton<IDsmTransportProbe, DsmTransportProbe>();
        registrations.AddHttpClient<INasConnectionTester, SynologyConnectionTester>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false
        }).RemoveAllLoggers();
        registrations.AddTransient<NasConnectionsViewModel>();
        registrations.AddSingleton<IDesktopWidgetHost>(_ => OperatingSystem.IsWindows()
            ? new WindowsDesktopHost()
            : OperatingSystem.IsMacOS()
                ? new MacDesktopHost()
                : new UnsupportedHost());
        registrations.AddSingleton(provider => new WidgetWorkspace(
            provider.GetRequiredService<IDesktopWidgetHost>(),
            provider.GetRequiredService<WidgetSettingsStore>(),
            provider.GetRequiredService<WidgetMonitoringPresenter>(),
            smokeMode));
        return registrations.BuildServiceProvider();
    }

    private sealed class UnsupportedHost : IDesktopWidgetHost
    {
        public DesktopAttachResult Attach(NativeWidget widget,
            NativeBounds bounds,
            bool locked,
            CancellationToken cancellationToken)
        {
            return new DesktopAttachResult(false, "unsupported-os");
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

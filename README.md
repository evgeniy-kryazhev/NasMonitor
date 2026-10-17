# NAS Monitor

Cross-platform Synology NAS monitoring with C#, .NET 10 and Avalonia UI, targeting Windows and macOS.

## Current application

- Add and edit multiple NAS profiles with HTTPS endpoints.
- Connect with DSM username/password and an optional one-time code.
- View CPU, RAM, per-interface network rates, volume capacity and disk status/temperature when DSM exposes them.
- Share one polling worker per NAS across the dashboard and widget. Network failures retain stale readings; authentication failures stop automatic login retries.
- Keep passwords in memory, or explicitly save them in Windows Credential Manager/macOS Keychain. Profile files contain metadata only.
- Open a widget for the selected NAS. A separate demo widget remains available under the Desktop widget tab.

This is an initial monitoring implementation. Protocol tests use synthetic DSM responses; compatibility with DS216+II/DS1821+ and actual DSM installations still needs device testing. Experimental Windows/macOS desktop placement also remains unaccepted. Development of the application proceeds alongside those platform checks.

## Run

Install .NET 10 SDK. Open `NasMonitor.sln` in Visual Studio or Rider; choose `NasMonitor.App` as the startup project. From the repository root:

```bash
dotnet restore NasMonitor.sln
dotnet build NasMonitor.sln -c Release
dotnet run --project src/NasMonitor.App/NasMonitor.App.csproj
```

In **NAS monitoring**, choose **+ New NAS**, enter a name, an HTTPS address such as `https://nas.local:5001/`, and DSM credentials, then click **Connect / save NAS**. CPU/storage API access depends on DSM permissions; the app reports partial/unavailable metrics instead of inventing values. Use **Edit / reconnect** to enter fresh credentials or an OTP, **Disconnect** to stop polling, and **Remove NAS** to delete the profile and its saved credential.

The NAS certificate must be trusted by the OS. HTTP and redirects are rejected; no certificate-validation bypass is offered. Default Synology self-signed certificates may require installing an appropriate trust chain or configuring a trusted certificate.

**Show widget** displays the selected NAS snapshot without creating another poller. Currently one widget is managed by the main window; switching NAS replaces that widget while all NAS cards continue monitoring. Desktop attachment is explicit and experimental. Use **Restore widget to normal window** from the Desktop widget tab if the widget's own controls are inaccessible.

## Validate and download

```bash
dotnet test NasMonitor.sln -c Release
```

CI runs shared protocol/monitoring/headless UI tests on Linux, Windows and macOS arm64/x64, real Win32 and Credential Manager checks on Windows, and an NSWindow/AppKit property smoke on macOS. Windows-only tests are explicitly skipped elsewhere. CI provides unsigned self-contained Windows/macOS archives; see [download and desktop validation instructions](docs/DESKTOP_HOSTS.md#automated-native-checks-and-downloadable-builds).

Read [AGENTS.md](AGENTS.md), [architecture](docs/ARCHITECTURE.md), [roadmap](docs/ROADMAP.md), [DSM limitations](docs/DSM.md) and [platform limitations](docs/DESKTOP_HOSTS.md) before development. Neither passing CI nor an AppKit property smoke establishes interactive desktop behavior or real-device DSM compatibility.

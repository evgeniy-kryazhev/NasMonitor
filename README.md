# NAS Monitor

Cross-platform desktop NAS monitoring based on **.NET 10** and **Avalonia UI**.

## Current milestone — Desktop Widget Spike

This is an **early technical prototype**, not a finished NAS monitor. It displays demo telemetry and allows opening a separate widget window. On Windows it can attempt an experimental Explorer desktop attachment. An experimental macOS AppKit desktop-level host is also available. Neither native host has passed interactive desktop acceptance; see `docs/DESKTOP_HOSTS.md`.

### Build

Install .NET 10 SDK, then:

```bash
dotnet restore src/NasMonitor.App/NasMonitor.App.csproj
dotnet run --project src/NasMonitor.App/NasMonitor.App.csproj
```

For Windows or macOS desktop attachment, press **Try desktop attachment** inside the widget. This is experimental and should not be assumed stable on all OS versions. Use **Restore normal window** to roll back native changes.

Read `AGENTS.md` and `docs/ARCHITECTURE.md` before continuing.

### Validation

```bash
dotnet build src/NasMonitor.App/NasMonitor.App.csproj --configuration Release
dotnet test tests/NasMonitor.App.Tests/NasMonitor.App.Tests.csproj --configuration Release
```

Tests exercise lifecycle behavior with fake native APIs and the shared Avalonia UI headlessly. They do not establish native desktop placement. See [platform limitations and pending acceptance checks](docs/DESKTOP_HOSTS.md). DSM work remains gated on that acceptance.

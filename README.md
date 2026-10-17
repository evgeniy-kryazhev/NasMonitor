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

### Native checks and desktop testing

GitHub Actions additionally tests native Win32 primitives on Windows and a real Avalonia NSWindow/AppKit property round trip on macOS arm64 and x64. These are API smoke checks, not visual desktop acceptance. The native test project is `tests/NasMonitor.NativeTests`; its Win32 tests are explicitly skipped off Windows.

Successful CI runs provide unsigned self-contained Windows/macOS archives for manual testing. See [native checks, download instructions and acceptance matrix](docs/DESKTOP_HOSTS.md#automated-native-checks-and-downloadable-builds). No real NAS credentials are needed for the demo.

# NAS Monitor

Cross-platform desktop NAS monitoring based on **.NET 10** and **Avalonia UI**.

## Current milestone — Desktop Widget Spike

This is an **early technical prototype**, not a finished NAS monitor. It displays demo telemetry and allows opening a separate widget window. On Windows it can attempt an experimental Explorer desktop attachment. On macOS the desktop host is not yet implemented; the app explicitly reports this limitation.

### Build

Install .NET 10 SDK, then:

```bash
dotnet restore src/NasMonitor.App/NasMonitor.App.csproj
dotnet run --project src/NasMonitor.App/NasMonitor.App.csproj
```

For Windows desktop attachment, press **Try desktop attachment** inside the widget. This is experimental and should not be assumed stable on all Explorer versions.

Read `AGENTS.md` and `docs/ARCHITECTURE.md` before continuing.

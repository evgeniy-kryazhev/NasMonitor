# Architecture

## Target layers

- `NasMonitor.App`: Avalonia windows, view models and application composition.
- `NasMonitor.Core`: provider-neutral models and interfaces (planned).
- `NasMonitor.Providers.Synology`: DSM auth, API discovery and telemetry (planned).
- `NasMonitor.Platform.Windows`: Win32/Explorer desktop host (initial experiment is currently in App).
- `NasMonitor.Platform.MacOS`: AppKit desktop host (experiment currently in App).

## Desktop requirement

Desktop attachment is different from an ordinary non-topmost floating window. Hosts must make widget content visible on desktop, obscured by ordinary app windows, while handling multi-monitor geometry, display scale changes, Explorer/Finder lifecycle, and user interaction.

Windows WorkerW is unsupported/undocumented by Microsoft. The first spike must explicitly verify behavior on real builds. macOS must be verified for Finder, Spaces, Mission Control, Stage Manager and full-screen apps.

## Planned monitoring

One polling coordinator per NAS, not one poller per widget. Provider snapshots are immutable and timestamped. Offline/error is distinct from a 0% reading. Views observe cached snapshots, never call DSM endpoints directly.

## Acceptance

Windows and macOS desktop attachment are **not accepted** until real OS validation succeeds. CI builds alone only establish compilation.

## Current desktop boundary

`IDesktopHost` exposes attachment, health checks and reversible detachment on the UI thread. `WindowsExplorerDesktopHost` and `MacOSDesktopHost` hold native snapshots; native API interfaces allow tests on other operating systems. `WidgetWindow` owns the timer and stops it on close. `MainWindow` handles bounded recreation after a destroyed HWND. Both platforms share the same Avalonia controls and demo data. Native calls stay under `App/Platform` until extraction into dedicated platform projects is justified.

Read [desktop host evidence, limitations and acceptance matrix](DESKTOP_HOSTS.md) before treating either experiment as accepted. There is no Linux desktop integration; Linux is used for build and headless tests.

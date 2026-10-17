# Architecture

## Target layers

- `NasMonitor.App`: Avalonia windows, view models and application composition.
- `NasMonitor.Core`: provider-neutral models and interfaces (planned).
- `NasMonitor.Providers.Synology`: DSM auth, API discovery and telemetry (planned).
- `NasMonitor.Platform.Windows`: Win32/Explorer desktop host (initial experiment is currently in App).
- `NasMonitor.Platform.MacOS`: AppKit desktop host (planned).

## Desktop requirement

Desktop attachment is different from an ordinary non-topmost floating window. Hosts must make widget content visible on desktop, obscured by ordinary app windows, while handling multi-monitor geometry, display scale changes, Explorer/Finder lifecycle, and user interaction.

Windows WorkerW is unsupported/undocumented by Microsoft. The first spike must explicitly verify behavior on real builds. macOS must be verified for Finder, Spaces, Mission Control, Stage Manager and full-screen apps.

## Planned monitoring

One polling coordinator per NAS, not one poller per widget. Provider snapshots are immutable and timestamped. Offline/error is distinct from a 0% reading. Views observe cached snapshots, never call DSM endpoints directly.

## Acceptance

Windows and macOS desktop attachment are **not accepted** until real OS validation succeeds. CI builds alone only establish compilation.

# Architecture

## Target layers

- `NasMonitor.App`: Avalonia windows, view models and application composition.
- `NasMonitor.Core`: provider-neutral profiles, immutable snapshots, session interfaces and the shared polling service.
- `NasMonitor.Providers.Synology`: read-only DSM discovery, authentication/session handling and CPU/RAM/network/storage telemetry.
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

## Monitoring application

The main window composes `MonitoringWorkspace`, the OS credential store, an atomic JSON metadata store and `NasMonitorService`. Its NAS tab supports multiple profiles, connection editing, disconnect/removal and per-NAS metric cards. A selected NAS can be shown in the shared widget window; currently the main window manages one visible widget at a time. Views consume cached updates and never perform DSM requests themselves.

The workspace serializes connect/reconnect/disconnect/remove operations. Each NAS gets one session and one sequential worker; reconnect waits for the previous worker to stop. Reads have HTTP timeouts, network failures back off up to 120 seconds, and authentication failures pause until explicit reconnection. Last successful immutable snapshots remain distinct from unknown metrics and stale/offline state. A window close unsubscribes its metrics listener without stopping NAS monitoring. App shutdown cancels workers, awaits disposal and attempts bounded DSM logout.

Profile JSON includes identifiers, labels, HTTPS endpoints, usernames and a remember flag. Passwords are in memory for the session, optionally in Windows Credential Manager or the user's macOS login Keychain. Neither SID nor OTP is persisted. OS-vault operations can fail or prompt; there is no plaintext fallback. Tests use isolated temporary metadata and fake credentials/sessions; native Windows Credential Manager has a separate round-trip test.

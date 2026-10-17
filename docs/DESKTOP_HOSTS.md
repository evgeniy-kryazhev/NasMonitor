# Desktop host experiment and acceptance

## Evidence and current status

Both native hosts are **experimental and not accepted**. Development checks in Linux validate compilation, lifecycle logic using fake native APIs, and shared UI through Avalonia Headless. They do not validate native P/Invoke, actual placement, focus, DPI, Spaces, Explorer/Finder lifecycle, or desktop interaction. No Windows or macOS interactive desktop has been tested for this change. Local Release build and 14 tests passed (12 lifecycle tests and 2 headless UI tests, none skipped). Framework-dependent cross-publish for win-x64, osx-arm64 and osx-x64 also passed on Linux; this checks packaging, not execution on the target OS.

At the initial spike commit `f1e7dc3`, GitHub Actions push run [39013797379](https://github.com/evgeniy-kryazhev/NasMonitor/actions/runs/39013797379) and PR #1 run [39013811485](https://github.com/evgeniy-kryazhev/NasMonitor/actions/runs/39013811485) reported Success. The original Linux build also passed, with NU1903 for Tmds.DBus.Protocol 0.21.2. Updating Avalonia packages together to 11.3.22 removed that warning; NuGet audit found no known vulnerable packages at validation time. Hosted runner builds are not desktop acceptance.

Application and DSM development now proceed in parallel with this unaccepted desktop experiment. The NAS dashboard and bound widgets use real provider snapshots; the separate demo widget remains explicitly marked DEMO. Real-device DSM validation and interactive desktop acceptance are both outstanding and must not be inferred from CI.

## Common UI and lifecycle

One Avalonia WidgetWindow is used on both platforms. Only IDesktopHost manipulates native window state. Attach and restore are explicit UI actions. Native calls and a two-second health timer run on the UI thread; failures back off up to 30 seconds. Closing restores native state before Avalonia destroys the window. Closing stops the timer. A missing or unexpected native handle does not attempt native calls.

The native host interface has deterministic tests for snapshot preservation, repeated attachment, rollback including failed rollback, parent replacement, unavailable Explorer, destroyed widget handles, restoration, and unsupported platforms. Headless UI tests check the shared demo view, unavailable-handle path, restore action, and close. These tests deliberately do not assert native desktop success.

## Windows

The WorkerW/Progman protocol is undocumented. Discovery uses a bounded SendMessageTimeout, finds the WorkerW after the window containing SHELLDLL_DefView, and makes no change if it cannot find a target. There is no fallback that claims a normal non-topmost window is attached.

Before mutation, capture parent, GWL_STYLE, GWL_EXSTYLE, and screen bounds once. Attachment changes WS_POPUP/WS_CHILD and tool-window flags, reparents, translates screen coordinates into the desktop parent's coordinates, and applies FRAMECHANGED without activation. Detach restores the snapshot; if the old parent is gone, restore to a top-level window. Rollback failures retain the snapshot for a retry. Native Win32 failures are reported with error codes.

A UI timer detects a missing/replaced parent, restores to normal mode while Explorer is absent, and attempts reattachment when WorkerW returns. If Explorer destroys the child HWND, the main window recreates the Avalonia widget rather than reusing the destroyed handle. Automatic recreation is limited to three retries, followed by a normal replacement window; the user may explicitly retry attachment. Actual Avalonia behavior after Explorer destroys its HWND is an unverified risk.

Limitations: Explorer internals vary across Windows builds; SetParent may fail with incompatible DPI awareness; virtual desktops, Show Desktop, child-window input/focus, transparency, native decorations, and display hotplug need interactive tests. Native style or parent changes may conflict with Avalonia's window management. After native restoration, the shared UI keeps a draggable title strip within a current monitor work area, moving a lost window to the nearest work area. Attached child HWNDs are not repositioned through top-level Avalonia coordinates on display changes; use the main-window restore action first. No guarantee is made for kiosk shells, non-Explorer shells, RDP, or secure desktops.

## macOS

Avalonia 11.3.22 exposes NSWindow through its native platform handle. The host requires that descriptor and uses pointer-sized Objective-C messaging, so the same code compiles for x64 and arm64. Avalonia owns the NSWindow: the host never releases it, changes its delegate, or replaces its content view.

The host saves NSWindow.level and collectionBehavior, sets the level to CGWindowLevelForKey(kCGDesktopWindowLevelKey) + 1 (above wallpaper, below icons/normal windows), and requests CanJoinAllSpaces | Stationary | IgnoresCycle. It reasserts these properties without repeatedly ordering or activating the window. Detach restores both saved properties. It does not call orderFrontRegardless, activate Finder, or add FullScreenAuxiliary.

This is desktop-level placement, **not embedding into Finder**. Finder restart should not own/destroy the window; that expectation still requires testing. CanJoinAllSpaces is a request to the window server, not proof of correct Mission Control or Stage Manager behavior. No policy promises visibility over exclusive full-screen apps. Icons can overlap the widget; keyboard interaction, Dock activation, window ordering, wallpaper interaction, and Mission Control presentation must be checked. No private Finder APIs, accessibility permission, or injection are used.

## Interactive validation matrix (all pending)

Record OS version/build, architecture, Avalonia version, monitor layout/scales, actions, result, and video/screenshots where useful. Report failures separately from unsupported behavior.

| Platform | Required checks | Result |
| --- | --- | --- |
| Windows 10 and 11 | Attach; normal apps obscure widget; widget survives focus changes and Show Desktop; desktop icons remain usable | Not run |
| Windows | Repeated attach/restore; close attached; restore style, owner, position and dimensions; no taskbar/focus surprises | Not run |
| Windows | Restart Explorer while attached, absent and returning; verify no crash, orphan, duplicate or infinite recreation | Not run |
| Windows | Multiple monitors, negative coordinates, mixed DPI, scaling changes, hotplug, sleep/wake, virtual desktops | Not run |
| macOS arm64 and x64 | Level above wallpaper/below normal apps and icons; click/input behavior; restore/close/reopen | Not run |
| macOS | Finder relaunch; Spaces switching and creation; Mission Control entry/exit; Show Desktop | Not run |
| macOS | Stage Manager on/off, full-screen transition and return, app activation and Dock/Command-Tab behavior | Not run |
| macOS | Multiple displays, separate Spaces setting, Retina/non-Retina scaling, hotplug, sleep/wake | Not run |

Use `dotnet run --project src/NasMonitor.App/NasMonitor.App.csproj`, open the demo widget, and explicitly attach. Use Restore normal window before evaluating restoration. CI compilation and unit tests cannot fill in this table.

## Application monitoring (in progress)

Keep platform hosting separate from telemetry. The existing roadmap requires multiple NAS profiles, Windows Credential Manager/macOS Keychain for secrets, strict TLS, DSM API discovery and authentication (including supported 2FA), and immutable timestamped snapshots. One cancellable polling coordinator per NAS feeds every widget; offline/stale/error is distinct from a zero reading, with bounded timeouts and retry backoff. No passwords, tokens or DSM sessions belong in plaintext settings or logs.

## Automated native checks and downloadable builds

The workflow runs shared headless/lifecycle tests on Windows, Linux, macOS arm64, and macOS x64 (`macos-15-intel`). An additional `NasMonitor.NativeTests` project calls real user32 on Windows using private hidden STATIC windows, **not Explorer windows**. It checks owned/unowned top-level restoration, screen-coordinate preservation through parenting, and rollback after an invalid-parent failure. These tests are explicitly skipped on other OSes; skipped tests are not native validation.

Owner and parent are different concepts: GetParent can return an owner for a top-level window. The snapshot therefore records WS_CHILD parent and GW_OWNER separately, and restores a top-level owner through GWLP_HWNDPARENT after returning to top-level mode. A missing original child parent falls back to a top-level window rather than leaving WS_CHILD without a parent.

On macOS the workflow starts the real Avalonia application with:

```bash
dotnet run --project src/NasMonitor.App/NasMonitor.App.csproj -c Release -- --native-host-smoke artifacts/appkit-smoke.json
```

This mode creates a real NSWindow on the main UI thread, applies the native properties twice, reads them back, restores the original properties, checks equality and exits with 0 on success or 1 on failure. Invalid/non-macOS invocation exits with 2. CI bounds the run to two minutes, so a missing WindowServer session or a hung native initialization fails rather than being silently treated as success. The JSON report records architecture, OS, levels, collection behaviors and outcome; it includes no NAS credentials or endpoint details. A crash before report writing is still a failed CI step, not a skipped check.

These native tests validate API compatibility and local state transitions. Even if green, they do **not** validate WorkerW discovery, Explorer restart, visual ordering, Finder behavior, Spaces transitions or Mission Control. The interactive matrix above remains pending until a human tests it on the corresponding desktop.

Successful Windows/macOS jobs also upload unsigned, self-contained development archives under Actions artifacts:

- `NasMonitor-win-x64-unsigned`
- `NasMonitor-osx-arm64-unsigned`
- `NasMonitor-osx-x64-unsigned`

Download the artifact for your OS/architecture from a successful workflow run, unpack its ZIP and then the `.tar.gz` inside it. On Windows start `NasMonitor.App.exe`; on macOS start `./NasMonitor.App` from Terminal. No separately installed .NET runtime is required. The inner tar archive preserves Unix execute permissions. macOS smoke mode is also available as `./NasMonitor.App --native-host-smoke ./appkit-smoke.json`.

These archives are not signed/notarized installers or `.app` bundles. OS security controls may block downloaded builds; prefer building from the reviewed source locally in that case. Do not disable system-wide security controls. Packaging, signing and notarization remain a later milestone.

For each manual test, record the tested commit SHA, artifact RID, OS version, monitor scaling/layout and the observed outcome in a report. Keep `not run`, `passed`, `failed` and `unsupported` separate, and do not mark the desktop milestone accepted based on a native API smoke report alone. Do not restart Explorer/Finder automatically from CI or the application; perform lifecycle tests deliberately on a desktop where it is safe to interrupt the shell.


## Recovery from the main window

The Desktop widget tab offers **Restore widget to normal window**, independently of controls inside the widget. It mirrors native hosting status and retains errors so restore can be retried. Explicit restore cancels automatic reattachment even if native rollback fails. Opening an existing widget also restores it before activation; closing the main window closes the widget and prevents queued native recovery from reopening it.

Normal windows with a reachable title strip retain their positions, including negative coordinates and partial overlap across monitors. If that strip is lost, restore/recreation moves the window to the nearest valid work area using physical screen coordinates and display scaling. An oversized window is aligned with the work-area origin without forcing a resize. Missing screen information leaves position unchanged. Display-change notifications only relocate detached normal windows. These geometry rules have pure/headless tests; real mixed-DPI/hotplug behavior is still part of the pending manual matrix.

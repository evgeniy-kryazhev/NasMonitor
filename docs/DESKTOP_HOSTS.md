# Desktop host experiment and acceptance

## Evidence and gate

Both native hosts are **experimental and not accepted**. Development checks in Linux validate compilation, lifecycle logic using fake native APIs, and shared UI through Avalonia Headless. They do not validate native P/Invoke, actual placement, focus, DPI, Spaces, Explorer/Finder lifecycle, or desktop interaction. No Windows or macOS interactive desktop has been tested for this change. Local Release build and 14 tests passed (12 lifecycle tests and 2 headless UI tests, none skipped). Framework-dependent cross-publish for win-x64, osx-arm64 and osx-x64 also passed on Linux; this checks packaging, not execution on the target OS.

At the initial spike commit `f1e7dc3`, GitHub Actions push run [39013797379](https://github.com/evgeniy-kryazhev/NasMonitor/actions/runs/39013797379) and PR #1 run [39013811485](https://github.com/evgeniy-kryazhev/NasMonitor/actions/runs/39013811485) reported Success. The original Linux build also passed, with NU1903 for Tmds.DBus.Protocol 0.21.2. Updating Avalonia packages together to 11.3.22 removed that warning; NuGet audit found no known vulnerable packages at validation time. Hosted runner builds are not desktop acceptance.

Do not begin the DSM integration milestone until representative Windows and macOS desktop acceptance evidence is recorded. There is no real NAS connectivity in this spike; all displayed telemetry remains explicitly marked DEMO.

## Common UI and lifecycle

One Avalonia WidgetWindow is used on both platforms. Only IDesktopHost manipulates native window state. Attach and restore are explicit UI actions. Native calls and a two-second health timer run on the UI thread; failures back off up to 30 seconds. Closing restores native state before Avalonia destroys the window. Closing stops the timer. A missing or unexpected native handle does not attempt native calls.

The native host interface has deterministic tests for snapshot preservation, repeated attachment, rollback including failed rollback, parent replacement, unavailable Explorer, destroyed widget handles, restoration, and unsupported platforms. Headless UI tests check the shared demo view, unavailable-handle path, restore action, and close. These tests deliberately do not assert native desktop success.

## Windows

The WorkerW/Progman protocol is undocumented. Discovery uses a bounded SendMessageTimeout, finds the WorkerW after the window containing SHELLDLL_DefView, and makes no change if it cannot find a target. There is no fallback that claims a normal non-topmost window is attached.

Before mutation, capture parent, GWL_STYLE, GWL_EXSTYLE, and screen bounds once. Attachment changes WS_POPUP/WS_CHILD and tool-window flags, reparents, translates screen coordinates into the desktop parent's coordinates, and applies FRAMECHANGED without activation. Detach restores the snapshot; if the old parent is gone, restore to a top-level window. Rollback failures retain the snapshot for a retry. Native Win32 failures are reported with error codes.

A UI timer detects a missing/replaced parent, restores to normal mode while Explorer is absent, and attempts reattachment when WorkerW returns. If Explorer destroys the child HWND, the main window recreates the Avalonia widget rather than reusing the destroyed handle. Automatic recreation is limited to three retries, followed by a normal replacement window; the user may explicitly retry attachment. Actual Avalonia behavior after Explorer destroys its HWND is an unverified risk.

Limitations: Explorer internals vary across Windows builds; SetParent may fail with incompatible DPI awareness; virtual desktops, Show Desktop, child-window input/focus, transparency, native decorations, and display hotplug need interactive tests. Native style or parent changes may conflict with Avalonia's window management. Restoring uses the original rectangle, which might now be outside the work area after monitor removal. No guarantee is made for kiosk shells, non-Explorer shells, RDP, or secure desktops.

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

## Next milestone after acceptance

Keep platform hosting separate from telemetry. The existing roadmap requires multiple NAS profiles, Windows Credential Manager/macOS Keychain for secrets, strict TLS, DSM API discovery and authentication (including supported 2FA), and immutable timestamped snapshots. One cancellable polling coordinator per NAS feeds every widget; offline/stale/error is distinct from a zero reading, with bounded timeouts and retry backoff. No passwords, tokens or DSM sessions belong in plaintext settings or logs.

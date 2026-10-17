# Development instructions

- Product: NAS Monitor. C# / .NET 10 / Avalonia UI, Windows 10/11 and macOS (arm64/x64).
- Critical requirement: widget is placed on the **desktop behind ordinary application windows**, not simply `Topmost=false`.
- Never mark desktop integration as implemented or verified without real Windows and macOS desktop tests.
- Separate Avalonia presentation from platform-specific desktop hosting.
- For Windows, WorkerW/Progman integration is experimental and undocumented; handle Explorer restarts and preserve rollback paths.
- For macOS, investigate AppKit/NSWindow and Spaces; do not confuse lowering a window with true desktop attachment.
- Develop multi-NAS support, the Synology DSM provider, secure credential storage and monitoring in parallel with the desktop spike, per the current product direction. Native desktop acceptance remains a separate, explicitly unverified milestone.
- Never store DSM passwords, API tokens or session identifiers in plaintext settings or logs.
- Use strict TLS validation by default; non-admin access should be evaluated before requiring admin privileges.
- Implement cancellation, timeouts, retry backoff and accurate stale/offline status.
- New features require tests or explicit test gaps and documentation.
- Avoid unrelated changes on main; develop in branches and submit PRs.

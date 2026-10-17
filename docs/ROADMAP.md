# Roadmap

1. Desktop spike (parallel track): Windows WorkerW and macOS AppKit experiments exist; interactive placement, lifecycle and multi-monitor acceptance remain pending.
2. Profiles (MVP implemented): multiple NAS, HTTPS endpoints and optional Windows Credential Manager/macOS Keychain storage. Automatic LAN/WAN failover and profile import/export remain planned.
3. Synology DSM (first adapter implemented): API discovery, login/OTP, session renewal and CPU/RAM/network/volume/disk snapshots. Validate schemas, permissions and units on real DSM devices; broader health/2FA flows remain planned.
4. Monitoring (MVP implemented): shared sequential polling, cached snapshots, offline/stale/degraded states and backoff. Chart history and configurable cadence remain planned.
5. Widgets: compact/standard/detailed, transparent appearance, configuration, saved location, multi-NAS view.
6. Packaging: signed/notarized builds where appropriate, startup/tray, Windows/macOS x64 and arm64 verification.

Hardware targets for compatibility evaluation: Synology DS216+II and DS1821+ with DSM 7.1+.

# Roadmap

1. Desktop spike: prototype Windows WorkerW and macOS AppKit hosts, verify native behavior and fallback.
2. Profiles: multiple NAS, LAN/WAN endpoint selection, TLS, credentials in Windows Credential Manager and macOS Keychain.
3. Synology DSM: discover API versions, authenticate (including 2FA where supported), collect CPU, RAM, network, storage and health.
4. Monitoring: shared polling, caching, offline/stale states, chart history, backoff.
5. Widgets: compact/standard/detailed, transparent appearance, configuration, saved location, multi-NAS view.
6. Packaging: signed/notarized builds where appropriate, startup/tray, Windows/macOS x64 and arm64 verification.

Hardware targets for compatibility evaluation: Synology DS216+II and DS1821+ with DSM 7.1+.

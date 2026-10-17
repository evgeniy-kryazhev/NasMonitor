# DSM monitoring MVP

## Requests and compatibility

The adapter discovers `SYNO.API.Auth`, `SYNO.Core.System.Utilization`, `SYNO.Storage.CGI.Storage`, and legacy `SYNO.Core.Storage.Volume`/`SYNO.Core.Storage.Disk` through `SYNO.API.Info`. Discovery paths must be simple CGI filenames and stay under the profile's HTTPS `/webapi/` prefix, including a configured reverse-proxy base path. All requests use POST; credentials, OTP and SID are form fields, not URL parameters. Redirects are rejected to avoid forwarding credentials to another destination. Responses are buffered with a 4 MiB limit, requests time out after 15 seconds, and normal OS TLS verification stays enabled.

Authentication uses the intersection of advertised API versions with the supported Auth version (up to 7). Telemetry currently targets version 1. Login returns a SID; the password stays in memory for session renewal. One expired-session retry reauthenticates, without reusing the OTP. Unsupported versions and permissions are reported explicitly. Auth errors pause polling rather than repeatedly submitting a bad password. Secure SignIn push approval, passwordless login, QuickConnect resolution and certificate pinning are not implemented. An account requiring a fresh OTP must be reconnected through the form.

CPU is the sum of reported user/system/other percentages; RAM uses `real_usage`. Missing or invalid values remain unknown, not zero. Network RX/TX are interpreted as bytes/s per interface and displayed in binary units; interfaces are not summed, avoiding double counting bonds. Volumes read used/total byte fields and disks expose reported status/model/temperature. Storage Manager `load_info` is preferred when advertised; legacy list APIs are the fallback when absent. Storage permission/version failures leave usable CPU/RAM snapshots with a degraded warning.

These core/storage APIs are not a stable public DSM contract across every model/version. Initial schemas follow common DSM implementations and community references (including the [synology-api wrappers](https://github.com/N4S4/synology-api)); code is independently implemented. Synthetic test fixtures exercise the implemented shapes. Network units, storage shapes, API versions, non-admin permissions, 2FA and SID renewal must still be checked on a real NAS. No real DSM device has been used in this cloud task. Missing API support may require a future adapter revision rather than elevated privileges.

## Polling and lifecycle

Each configured NAS has one sequential session/worker, a default five-second pause after a completed read, and no overlapping polls. Transient failures back off up to 120 seconds. Snapshot timestamps represent completion of a successful read; the previous successful snapshot is retained and marked stale after network/authentication failure. Disconnected views do not report old numbers as live zero values. Cards and the selected widget subscribe to the same cache. Closing the widget does not disconnect the NAS. Closing the app cancels in-flight requests and attempts logout with a two-second deadline per session.

## Persistence

`profiles.json` is stored in `Environment.SpecialFolder.ApplicationData/NasMonitor`. It contains name, ID, HTTPS endpoint, username and a remember flag; usernames and NAS addresses are metadata and may identify private devices. It contains no password, SID or OTP. Writes use a temporary file and atomic replacement.

Credential saving is optional. Windows uses per-user Credential Manager generic credentials under `NasMonitor/<profile-id>`. macOS uses a generic-password item in the default login Keychain, service `NasMonitor`, account the profile ID. Keychain access may prompt or fail if locked; no plaintext fallback is used. The initial Keychain adapter uses the Security framework generic-password functions and still needs manual validation with the packaged app, including access-control prompts and future signing identity. Linux development supports session-only passwords and disables credential saving.

Passwords are managed strings in process memory; this implementation does not promise zeroization of all managed copies. Temporary native credential buffers are cleared where controlled. Credential string formatting is redacted. OTP is cleared after successful login, and credential textboxes are cleared after submitting a connection. Saving a profile/credential precedes asynchronous sign-in, so a failed login can still leave a saved profile and credential: edit/reconnect or remove it explicitly.

## Device check

1. Start with an HTTPS endpoint reachable from the device running the app, using a trusted certificate and an account with appropriate monitoring permissions.
2. Connect without remembering credentials; compare CPU/RAM, interface rates, volume sizes and disk status with DSM Resource Monitor/Storage Manager.
3. Add a second NAS and verify independent cards; open a widget and verify both consume matching cached snapshots.
4. Disconnect network access: previous values should remain timestamped/stale. Restore access and verify recovery. Enter an invalid password or OTP and verify automatic sign-in attempts stop.
5. Test optional OS-vault persistence, app restart/reconnection, profile removal and Keychain permission denial. Never put credentials or SID into a report.

Record DSM version/model, which APIs/fields were available, permission level and observed results. Keep desktop acceptance and NAS compatibility results separate.

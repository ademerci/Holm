Holm 2.4.0 — Brecqhou starts a new series focused on desktop security hardening across Windows, Linux, the relay, and CI, and discontinues Holm for Android and its desktop phone-tab integration. Browser QR-code phone file transfer and PC-to-PC Holm account sync remain available.

## Download

| File | For whom |
| --- | --- |
| **Holm-2.4.0-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-2.4.0-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Removed

- **Holm for Android and phone integration.** Holm for Android and the desktop phone tab and phone-status integration have been discontinued and removed. Browser QR-code phone file transfer and PC-to-PC Holm account sync remain available. Upgrading from 2.3.x cleanly strips legacy phone settings and ignores legacy phone notes without errors.

### Changed

- **Windows and received-file safety.** Locked native DLL searches to System32 and resolved `explorer.exe` via the Windows directory. Received files and downloads now show exact saved filenames and hide one-click *Open* for dangerous executable, script, shortcut, or installer extensions (offering *Show* instead).
- **Beam LAN and QR phone-transfer hardening.** Unverified LAN peers are marked `(LAN)`, cannot shadow authenticated account devices or be overwritten from another IP, and display a 6-digit verification code (SAS) on both sides alongside offer and idle timeouts. Browser QR phone transfers now enforce approved file-count and byte quotas, client IP pinning, per-IP connection caps, header and write timeouts, and a 30-minute lifetime ceiling.
- **Stronger friend invites and encrypted messaging.** Friend invite codes now use 80-bit PBKDF2-stretched codes (`HOLM-XXXX-XXXX-XXXX-XXXX`) with single-use redemption, revocation, persistent replay protection, strict NIST P-256 key validation, and public HTTPS inbox URLs without mirroring to `ntfy.sh` by default.
- **Relay and admin hardening.** Added per-IP and per-topic rate limits, streaming body caps, storage-free empty reads with automatic cleanup alarms, authenticated `wnf-*` inbox reads, non-evicting WebSocket limits, and constant-time rate-limited admin stats with strict security headers.
- **Media, network, and Linux privacy hardening.** Blocked SSRF to private and non-public IPs across GIF, cover, and relay fetches, bounded bitmap and cover decoding before allocation, moved temporary and state files to private `0700`/`0600` locations, switched Linux notifications to binary D-Bus monitoring, stopped Linux dictation from searching `~/Downloads`, and required SHA-256 verification for Windows Handy downloads.
- **Least-privilege CI workflows.** Scoped all GitHub Actions workflows to least-privilege permissions and pinned every action to a full commit SHA.

### Fixed

- **Tests stay out of your settings.** The tests use a folder of their own, and an inbox on this computer (only tests have one) that got into the settings is set back to Holm's relay.

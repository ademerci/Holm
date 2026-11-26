# Holm 2.4.0 — Brecqhou

Windows x64 MSIX version 2.4.0.0.

## Customer release notes

- **Security hardening across Windows, Beam, and messaging.** Native DLL loading is restricted to System32, and received files or downloads show exact saved filenames while hiding one-click Open for executable, script, shortcut, or installer extensions (offering Show instead).
- **Stronger Beam LAN and browser QR phone transfers.** Unverified LAN peers are marked (LAN) and display a 6-digit verification code on both sides with offer and idle timeouts. Browser QR phone transfers enforce approved file-count and byte quotas, client IP pinning, connection limits, and a 30-minute lifetime ceiling.
- **Stronger friend invites and encrypted messaging.** Friend invite codes now use 80-bit stretched codes with single-use redemption, revocation, replay protection, strict NIST P-256 key validation, and public HTTPS inbox URLs without mirroring to ntfy.sh by default.
- **Media and network safety.** GIF, cover art, and relay fetches block private/non-public IP addresses, bound image decoding before allocation, and store state in private locations.
- **Your PCs, anywhere.** Windows PCs signed in to the same Holm account appear in Beam as your devices for encrypted file and text transfers over the relay.
- **Android support discontinued.** Holm for Android and the desktop phone-tab integration have been discontinued and removed. Browser QR-code phone file transfer and PC-to-PC Holm account sync remain available.
- **Verity and reminders polish.** Verity has dedicated switches in Settings › General, sits beside the playing track, and reacts to petting, dragging, and feeding. Reminders handle "today", daylight-saving changes, and long scrollable lists cleanly.

## Validation

Verify Store-managed updates ("Updates are managed by Microsoft Store."), Start with Windows (`HolmStartup`) after signing back in, Handy opening the release page instead of downloading an installer, and an upgrade from the live Store package (`2.2.3.0`) before submission.

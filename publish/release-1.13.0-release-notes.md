Holm 1.13.0 runs on Windows and Linux. It is the same Holm, rebuilt on a cross-platform UI; your settings, friends and chats carry over.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.13.0-win-x64.zip** | Unzip and run `Holm.exe`; also used by Holm's updater. |
| **Holm.exe** | Run the self-contained app directly; no .NET installation needed. |
| **Holm-1.13.0-linux-x64.tar.gz** | Unpack and run `./Holm`; no .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64; or Linux x64 with X11 (Wayland through XWayland).

Holm is not code-signed. Windows SmartScreen may warn on first launch; Smart App Control may block an unsigned app.

### Added
- **Holm for Linux.** The notch, music, volume and Bluetooth, messages and notifications, Beam, friends and chat, the tools, weather and the AI usage rings work on Linux too (PulseAudio or PipeWire, BlueZ, NetworkManager, UPower). Copying text from screenshots uses `tesseract` when it is installed. On Linux, *Update* opens the release page.

### Changed
- **New foundation.** Holm is built with Avalonia instead of WPF, one app for both systems. It reads the same settings file, friends key, chats and achievements as 1.12, and keeps the same autostart entry, so updating from 1.12.11 keeps everything in place.

# Merging dev into main (Holm 1.13.0)

`dev` holds every commit of `main` (up to 1.12.11) plus the cross-platform app, so merging it into `main` is a
fast-forward without conflicts. After it, `main` builds the Avalonia app (`Holm.App`) for Windows and Linux and the
WPF app (`WinNotch/`) is gone. Every Windows user then gets the new app through the 1.12 updater.

## Done on dev

- 1.12.8 to 1.12.11 ported: AI usage rings and page, the classic look as the default and the instrument look as a
  setting, settings texts as in 1.12.11.
- Data carries over: the same settings file in `%APPDATA%\Holm`, the same DPAPI entropy for the friends key, the same
  chats, achievements and autostart entry (`HKCU\...\Run`, value `Holm`).
- Release workflow builds `Holm.exe`, `Holm-<version>-win-x64.zip` (only `Holm.exe`, `LICENSE`, `README.txt`, as the
  1.12 updater requires) and `Holm-<version>-linux-x64.tar.gz`; it fails if the Windows exe's file version isn't
  `<version>.0`, which the 1.12 updater checks. The cross-platform workflow checks the same on every test build.
- `publish/release-1.13.0-release-notes.md`, CHANGELOG entry (unreleased), README and AGENTS.md updated.
- Tests pass on Windows and Linux in CI; the self-test pictures of both systems are compared.

## Before merging: on a Windows PC

Use the test build from the latest cross-platform run (`Holm-1.13.0-test-build.<n>.exe`) or a dev release.

1. **Update from 1.12.11.** Install 1.12.11 (`Holm.exe` from its release) in a folder of your own, use it a little
   (a note, a reminder, a friend, autostart on), then run the 1.13 test build once *in place of it* (copy it over
   `Holm.exe`): settings, note, reminder, friends and chats must be there, autostart still on. The real updater path
   can only be tried once 1.13.0 is released (it downloads from the latest release); a prerelease is not offered to it.
2. **Every feature once**, in both looks (Settings › General › Instrument look, then restart): the notch over other
   windows and clicks passing beside it, music (Spotify or a browser), volume keys and *Volume and media only in the
   notch*, Bluetooth and output buttons, messages and all notifications, Ctrl+H, Caps Lock, downloads, screenshots
   (also Win+Shift+S without saving) and *Copy text*, USB stick, VPN, calls and the mic/camera dot, Beam to a friend
   and nearby, chat with a GIF, streaming the screen, timer and reminder sound, sleep and wake, dictation with Handy,
   Focumon, the AI button and rings, colour picker, mixer, clipboard history, Wi-Fi page and speed test.
3. **Footprint**: run `tools/Measure-Footprint.ps1` and compare idle RAM and CPU with 1.12.11.

## After merging: the Microsoft Store branch

`codex/microsoft-store-msix` packages the WPF app. After merging `main` into it, port its app changes to `Holm.App`:

- `PackageIdentity` (is Holm running as a package) — put it behind `Holm.Platform` (Windows) and use it for:
  - updates: no automatic check, *Updates are managed by Microsoft Store.* on the Updates page and in
    `UpdateInstaller.UnavailableReason`;
  - autostart: the package's `StartupTask` (`HolmStartup`) in place of the Run key, and no `Autostart.TakeOver`;
  - dictation: open Handy's release page instead of downloading its installer.
- `packaging/build-msix.ps1`: publish `Holm.App/Holm.App.csproj` (not `WinNotch\WinNotch.csproj`), read the version
  from it, and rename `Holm.App.exe` to `Holm.exe` before packing.
- `.github/workflows/store.yml`: the same project path.
- Build and test the package, increment the MSIX version, submit.

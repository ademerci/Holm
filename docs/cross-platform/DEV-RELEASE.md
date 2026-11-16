**⚠️ Alpha build for testing only – not a stable release.**

This is an alpha test build of the cross-platform rewrite of Holm (branch `dev`), for **Windows and Linux**. It is
meant to be tried and reported on, not relied on as a daily driver yet. Things may still differ from the Windows WPF
build or break on some Linux setups.

The stable Windows Holm (built from `main`) does not update to this build: its updater only takes full releases.

### Downloads

- **Windows x64:** `Holm-<version>-test-build.<n>.exe` – single self-contained file, no .NET needed. On Windows it
  reads the normal Holm's settings but does not overwrite them.
- **Linux x64:** `Holm-<version>-test-build.<n>-linux-x64` – single self-contained file, no .NET needed. Requires an
  X11 session (on Wayland through XWayland) with a compositing window manager for transparent rounded corners.

### Linux setup: what to install

The binary includes .NET, Avalonia and SkiaSharp, so you do **not** need the .NET SDK/runtime, but it dynamically
loads a few standard Linux desktop libraries and CLI helpers:

| Dependency | Purpose |
|---|---|
| `libicu` (`libicu78` / `libicu74` / `libicu70`) | Required by .NET globalization (app exits on startup without it) |
| `libICE.so.6`, `libSM.so.6` (`libice6`, `libsm6`) | Required by Avalonia X11 (`X11PlatformLifetimeEvents`) |
| `libX11`, `libXext`, `libXrandr`, `libXi`, `libXfixes`, `libfontconfig` | X11 overlay window, input hit-region, hotkeys, click-away, clipboard, fonts |
| `libpulse.so.0` (`libpulse0`) | Volume, per-app audio mixer, reactive equalizer, microphone dot (PulseAudio / PipeWire) |
| `dbus-monitor` (`dbus` / `dbus-x11`) | Message pop-outs and badges from other apps |
| `tesseract-ocr` (optional) | Copy text from screenshots (OCR) |
| `xdg-utils`, `udisks2` (optional) | Opening files/links (`xdg-open`) and ejecting USB drives (`gio` / `udisksctl`) |

#### Ubuntu / Debian / WSL2 (WSLg)

```bash
sudo apt update
sudo apt install -y \
  libice6 libsm6 libx11-6 libxext6 libxrandr2 libxi6 libxfixes3 libfontconfig1 \
  libpulse0 dbus-x11 xdg-utils udisks2 tesseract-ocr \
  $(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | head -n1)
```

*(On Ubuntu 26.04 the ICU package is `libicu78`; on Ubuntu 24.04 `libicu74`; on Ubuntu 22.04 `libicu70`.)*

#### Fedora

```bash
sudo dnf install -y \
  libicu libICE libSM libX11 libXext libXrandr libXi libXfixes fontconfig \
  pulseaudio-libs dbus-tools xdg-utils udisks2 tesseract
```

#### Arch Linux

```bash
sudo pacman -S --needed \
  icu libice libsm libx11 libxext libxrandr libxi libxfixes fontconfig \
  libpulse dbus xdg-utils udisks2 tesseract
```

#### Run on Linux / WSL2

```bash
chmod +x Holm-<version>-test-build.<n>-linux-x64
./Holm-<version>-test-build.<n>-linux-x64
```

On WSL2, copy the file into the Linux filesystem first (e.g. `/tmp/`) for fast startup:

```bash
cp /mnt/c/<path-to-file>/Holm-<version>-test-build.<n>-linux-x64 /tmp/holm
chmod +x /tmp/holm
/tmp/holm
```

### What's in this alpha

- Notch shape, springs, sizes, display selection, top-edge dragging & Reset Position, fullscreen/browser hiding
- Idle clock, big clock, today cards, weather, sky colours, AI usage bubble & hover card (Claude Code / Codex)
- Media controls, volume, per-app mixer, sound output, Bluetooth & battery, sleep timer, timer
- Tools: notes, reminders, calculator, clipboard history, colour picker, shelf, Focumon, AI button, dictation (Handy)
- Pop-outs: downloads, screenshots & OCR, USB drives, Caps Lock, VPN, connectivity, low disk space, notifications, password wipe
- Beam & Friends: file/folder/text beam, nearby, phone beam, send later, chat (with GIFs & reactions), waves, activity, achievements, profile editor, screen streaming, Wi-Fi & speed test, updates

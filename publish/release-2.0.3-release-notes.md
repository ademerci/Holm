Holm 2.0.3 — Herm keeps the notch on top of other windows again.

## Download

| File | For whom |
| --- | --- |
| **Holm-2.0.3-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-2.0.3-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Fixed

- **Always on top.** The notch stays above other windows again on Windows. Since 1.13, opening or clicking another window could leave the notch behind it. Holm now puts it back on top as soon as another window comes to the front.
- **Minimize over browser tabs.** The notch turns into the small pill only while a maximized browser is the window in front on the notch's screen. It reacts the moment you switch windows: it grows back as soon as you switch to another app and shrinks again when you return to the browser.

Holm 2.2.0 — Burhou lets you mute pop-outs from the right-click menu and uses much less memory.

## Download

| File | For whom |
| --- | --- |
| **Holm-2.2.0-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-2.2.0-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Added

- **Mute pop-outs.** Right-click the notch and choose *Mute pop-outs*: messages and notifications no longer pop out of the notch. They still count in the closed notch, and the open notch says that pop-outs are muted. Choose it again to switch them back on.

### Changed

- **Much less memory.** Holm now draws on the processor instead of the graphics card. Its windows are small, and the graphics card's drivers and caches cost far more memory than they saved. On Windows, an idle Holm drops from about 115–340 MB to about 50 MB of private memory, and it stays near 60 MB after you open and close the notch (it was over 200 MB). Should Holm draw badly on your system, start it with the environment variable `HOLM_GPU=1` to use the graphics card again.

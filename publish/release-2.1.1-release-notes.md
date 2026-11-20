Holm 2.1.1 — Lihou uses far less memory, fixes notch clipping, restores volume feedback, and makes the phone QR card easier to use.

## Download

| File | For whom |
| --- | --- |
| **Holm-2.1.1-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-2.1.1-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Changed

- **Much less memory.** Holm now draws on the processor instead of the graphics card. Its windows are small, and the graphics card's drivers and caches cost far more memory than they saved. On Windows, an idle Holm drops from about 115–340 MB to about 50 MB of private memory, and it stays near 60 MB after you open and close the notch (it was over 200 MB). Should Holm draw badly on your system, start it with the environment variable `HOLM_GPU=1` to use the graphics card again.

### Fixed

- **Notch sizing.** The sides stay visible during bouncy animations, and the notch scales down to fit narrow or high-DPI displays.
- **Volume keys.** With *Volume and media only in the notch* on, Windows volume and mute keys show the level in Holm, even when the open view hides its volume slider.
- **Phone QR card.** Hovering over the card keeps it open. Long transfer messages leave room for **Done**, **Copy link**, and the receive buttons.
- **Add Friend.** The full invite code placeholder fits and sits in the middle of the field.
- **Media controls.** Previous, Play, Pause, and Next sit in the center of their hover backgrounds.

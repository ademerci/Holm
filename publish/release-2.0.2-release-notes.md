Holm 2.0.2 — Herm fixes Friends requests and improves reliability on Windows and Linux.

## Download

| File | For whom |
| --- | --- |
| **Holm-2.0.2-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-2.0.2-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Fixed

- **Friend requests.** Resending a request keeps the original acceptance valid. Unanswered incoming requests stay available after restarting Holm.
- **Settings.** Interrupted saves preserve the previous settings file and notes.
- **Reminders.** Invalid times and durations show a validation error.
- **App volume.** Mixer operations run in order and apply pending changes before closing.
- **Clipboard.** Password clearing stops retrying after you copy something else.
- **Linux.** USB eject commands time out and report power-off failures. Album art can recover after a temporary loading error.


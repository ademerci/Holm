Holm 1.14.1 makes automatic updates work again.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.14.1-win-x64.zip** | Unzip and run `Holm.exe`; also used by Holm's updater. |
| **Holm.exe** | Run the self-contained app directly; no .NET installation needed. |
| **Holm-1.14.1-linux-x64.tar.gz** | Unpack and run `./Holm`; no .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64; or Linux x64 with X11 (Wayland through XWayland).

Holm is not code-signed. Windows SmartScreen may warn on first launch; Smart App Control may block an unsigned app.

### Fixed
- **Automatic updates work again.** Holm 1.13.0 and 1.14.0 couldn't install updates by themselves: the updater only accepts an app named Holm, and those builds carried another name inside. This version has the right name, so Holm 1.13.0 and 1.14.0 can update to it (and to everything after it) with *Update*.

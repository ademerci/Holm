Holm 1.15.2 makes Beam nearby safer: a silent device on your network can no longer block it, and incoming files say more about what they are.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.15.2-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-1.15.2-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Fixed
- **Beam nearby can't be blocked any more.** A device on the same network that connected and then sent nothing kept Beam busy until Holm restarted: other nearby files were turned away and dropping files, copied codes and friend requests stopped working. Holm now lets such a connection go after 30 seconds.

### Changed
- **Clearer questions for incoming files.** A file from someone nearby says *nearby, not verified*, since the name is only what their device says. An offer with a program in it, such as an `.exe`, `.msi` or script, says so.

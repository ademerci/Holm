Holm 1.12.3 ships a smaller Holm.exe and bigger app icons in the closed notch.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.12.3-win-x64.zip** | Unzip and run `Holm.exe`; also used by Holm's updater. |
| **Holm.exe** | Run the self-contained app directly; no .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

Holm is not code-signed. Windows SmartScreen may warn on first launch; Smart App Control may block an unsigned app.

### Changed
- **A smaller Holm.exe.** The self-contained exe drops from 213 to about 157 MB: the framework keeps its own precompiled code instead of being compiled again into one large image, with nothing left out.
- **Bigger app icons in the closed notch.** While notifications wait, their app icons in the closed notch are as big as the album art, so they read at a glance.

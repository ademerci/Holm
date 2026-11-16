Holm 1.12.7 lets you put the notch where you want it along the top of your screen, and labels Codex usage with the limit it actually reports.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.12.7-win-x64.zip** | Unzip and run `Holm.exe`; also used by Holm's updater. |
| **Holm.exe** | Run the self-contained app directly; no .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

Holm is not code-signed. Windows SmartScreen may warn on first launch; Smart App Control may block an unsigned app.

### Added
- **Put the notch where you want it.** *Settings › General › Drag notch position* lets you hold the notch by its top edge and move it left or right along the selected screen. Its position is remembered after a restart and stays put when dragging is switched off. The small *Reset Position* button beside the switch brings it back to the centre.

### Fixed
- **Codex usage follows the reported limit.** A weekly allowance could show as a five-hour limit because Holm assumed the first limit was always five hours. The card now reads each limit's actual duration from Codex's local logs, shows additional limits only when reported, and includes the date in reset times. Old readings say they may be outdated; passing the reset time no longer invents zero usage.


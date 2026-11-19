Holm 1.17.1, Lundy, keeps the notch whole when you switch monitors and lets you fold away the account card.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.17.1-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-1.17.1-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Changed
- **Close the account card.** The sign-in card on the profile page has a small cross. It folds the card to one line with *Sign in…* to bring it back, and Holm remembers it. On the first-start offer the cross means *Not now*.

### Fixed
- **The notch after switching monitors.** On a display with another scale, for example a laptop at 125 % and a monitor at 100 %, the open notch showed clipped into a shifted rectangle until Holm restarted. Holm now fits itself to the new display at once.

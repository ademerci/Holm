Holm 2.2.3 — Burhou smooths out notch transitions and makes light themes easier to read.

## Download

| File | For whom |
| --- | --- |
| **Holm-2.2.3-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-2.2.3-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Added

- **Mute pop-outs in Settings.** Find the same switch under *Settings › Pop-outs* and in the notch’s right-click menu. Unread counts still appear while pop-outs are muted.

### Changed

- **AI usage on the Start page.** Turn AI usage on or off under *Settings › Start page* and see its effect in the preview right away. The separate switch for the two closed-notch rings is still under General.
- **Smoother transitions.** Holm does less layout and drawing work while animating. On Windows, it uses finer timing during the animation to better follow the display refresh rate.
- **More Jelly.** The notch jiggles more when poked, and the Settings window wobbles more when dragged or clicked. Buttons respond before the wobble starts.

### Fixed

- **Closing and reopening the notch.** The notch settles without the final twitch when it closes, and rows keep their place if you reopen it mid-animation.
- **Changes while the notch is open.** The notch adjusts its height smoothly when controls appear. Unchanged battery and Bluetooth information no longer causes the device controls to redraw.
- **Old Discord notifications.** Dismissed notifications stay dismissed after restarting Holm. Older unread notifications remain in the count without popping out again.
- **Portable and Microsoft Store copies.** The portable Windows app won’t launch while the Microsoft Store copy is running.
- **Weather while music plays.** Click the small weather icon in the open notch to change your location.
- **Theme contrast.** Start page checkboxes, Account details, Chat shortcuts, avatar initials, and unread badges stay readable on light themes such as Frutiger Aero. The large clock’s default color now follows the selected theme.

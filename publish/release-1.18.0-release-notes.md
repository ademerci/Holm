Holm 1.18.0 — Herm starts a new series, lets you arrange the open notch's start page, moves friends to Holm's own server, puts the notification count on the left, lets you hide the AI usage rings and fixes the color picker on scaled displays.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.18.0-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-1.18.0-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Added
- **A new series, Herm.** The Holm window shows the name beside the version number.
- **AI usage rings switch.** You can now hide the two small usage rings at the ends of the closed notch while keeping the usage visible on the AI page of the open notch. *AI usage* still turns the whole feature off.
- **Arrange the start page.** *Settings › Start page* (or right-click › *Customize* › *Start page…*) lets you decide what the open notch shows and where. Drag its sections into another order (the tool bar, the player and clock, the reminder and note, the activity, the shelf, and the battery and volume), hide the tool bar, the two cards or the battery and volume, swap a section's left and right side, and put the tools of the bar in your order. A small preview shows the result, and each change applies at once. Until you change something the open notch looks as before, and *Reset to default* brings that back. With a Holm account the layout is the same on every computer.

### Changed
- **Friends on Holm's own server.** Invites, friend requests, chat and online status now go through Holm's relay instead of ntfy.sh's free tier, whose daily limit made short friend codes fail with "refused the note (429)". Messages left while a friend's Holm is closed now wait a week instead of 12 hours. Friends on older Holm versions keep reaching you: Holm still listens on ntfy.sh and tells them your new address by itself.
- **Notification count on the left.** In the closed notch the red count now always sits at the left, before the apps' logos, also beside the music and in the instrument layout.

### Fixed
- **Color picker on scaled displays.** At 150 % or 200 % scale the picker showed the screen zoomed in, so only part of it could be picked. It now shows the whole screen exactly as it was.

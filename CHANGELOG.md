# Changelog

## 1.1.1 – 2026-10-21

### Changed
- **Teams messages no longer poll every 2 seconds.** WinNotch now watches the folder of Windows' notification database and only reads notifications when it changes, with a check once a minute as a safety net (every 10 s if the folder can't be watched). The feature's CPU cost dropped from about 1–3 % to no measurable difference.
- `--diag` logs which apps currently have notifications in the notification center (app names and counts only, never message text), to help find out why Teams messages don't show up.

### Fixed
- **Right-click menu glitching:** the menu was filled after it had opened, so it visibly jumped when the AI entry turned into a submenu and the screen entries were added. It is now filled before it opens.
- The notch no longer pushes itself in front of an open right-click menu (it re-asserts "always on top" every 1.5 s, which made the menu flicker).

### Docs
- README explains that Teams must send its notifications to Windows (*Notification style: Windows* in the new Teams); otherwise WinNotch can't see them.

## 1.1.0 – 2026-10-21

### Added
- AI button in the open notch: opens Claude, Gemini, ChatGPT or any app you pick (installed app first, website otherwise).
- Teams messages: new messages pop out of the notch, a badge counts unread ones, the open notch shows the newest.
- Submenus and dark tooltips in the right-click menu.

## 1.0.0 – 2026-10-21

First release: media controls, album-colour backdrop, laptop and Bluetooth battery levels, live activities, fullscreen detection, screen picker, right-click menu.

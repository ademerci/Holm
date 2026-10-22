# Changelog

## Unreleased

### Fixed
- **Every ping from a paused Spotify cost a full refresh.** Spotify re-sends its timeline about every 4.5 seconds, also when nothing plays, and WinNotch answered every one with a full round trip to the player and a pass over the notch. Now a timeline on its own is only checked against the position the notch already shows, and an update only reaches the notch when something you can see changed: 1 update in 45 seconds of Spotify playing instead of 11. Paused, CPU went from about 0.05 % to 0.03 % and memory stays about 3 MB lower. Most of the climb after WinNotch trims its memory (to about 45 MB within a minute) comes from the checks it runs every minute, not from Spotify.
- The open notch made a new cover brush for every update. The compact and the open notch now share one per cover.
- Tests that repeated updates aren't passed on and keep their cover, backdrop and colour brushes.

## 1.4.0 – 2026-10-22

### Added
- **Messages from more apps:** WhatsApp, Discord, Outlook (new, classic and the old Mail app), Telegram, Signal and Slack pop out of the notch like Teams messages – also when they run in Chrome, Edge or Firefox. The unread badge takes the colour of the app, clicking the message opens the app (or the site), and looking at the app clears its messages. Right-click › *Messages* switches single apps off.
- **Downloads:** while a browser saves a file to Downloads (Chrome, Edge, Brave, Opera, Vivaldi, Firefox), the notch shows its name, size and speed – over music only for a moment when it starts, and as a row in the open notch. When it's done: *Open* or *Show*. Right-click › *Downloads* turns it off.
- Tests for recognising the apps (also in the browser), reading badges from a database laid out like Windows', the unread count, the browsers' download steps (Chrome, Firefox, cancelled, copied) and – on request – real silent notifications through the notification center.

### Fixed
- **Teams messages never showed up** when Teams uses its own notification style: then it sends nothing to Windows' notification center. WinNotch now also reads the number on Teams' taskbar icon (works in every style, no notification access needed) and shows *Microsoft Teams · N unread messages*. For names and text, set Teams' notification style to Windows; the menu says so while Teams only sends numbers.
- A Beam test failed about one run in four: it changed the last character of a sealed note, which may only carry padding bits. It now changes a character that always counts.

### Changed
- The setting *Teams messages* is now *Messages* and carries over.

## 1.3.1 – 2026-10-22

### Added
- **Independent sizes:** right-click › *Collapsed size* and *Expanded size* each offer Small (85 %), Normal (100 %), Large (115 %) and Extra large (130 %).
- 37 regression test cases covering saved size preferences, migration, all 16 size combinations and transitions between states.

### Changed
- The previous shared size preference carries over to both new settings. Pop-outs and Beam prompts follow the collapsed size.
- The notch smoothly transitions between the two sizes while staying centred, with room for the outgoing view during animation.
- The README and contributor credits acknowledge GPT-6 Astra working alongside Claude on the project.

## 1.3.0 – 2026-10-22

### Added
- **Friends.** Add someone once with a friend code (right-click › *Friends › Copy my friend code*, they copy it, their notch offers to add you, yours offers to add them back) and they appear in the share view whenever you drag files onto the notch. Dropping files on a friend works anywhere, also on networks that keep devices apart: WinNotch makes a one-time code as with *Code* and leaves it, sealed for that friend, in their inbox on ntfy.sh; their notch picks it up and asks *Accept* / *Decline*. Only small sealed notes go through ntfy.sh, files still go through the relay, so there's no new size limit.
  - Notes are sealed with AES-256-GCM under a key the two friends derive from their key pairs (ECDH P-256); notes from anyone you haven't added are ignored, and nobody can write in a friend's name. The private key is stored with DPAPI.
  - A friend who is busy or in a fullscreen game declines straight away instead of leaving you waiting.
- **Size:** right-click › *Size* makes the whole notch smaller or bigger (85 % to 130 %).
- Tests for friend codes, sealed notes (forged sender, other inbox, tampering) and two friends meeting through ntfy and the relay.

### Changed
- **AirDrop is now called Beam** (AirDrop is Apple's name). The setting carries over.

### Fixed
- Received file names lose invisible text-direction marks, so `holiday‮gpj.exe` can no longer pose as `holidayexe.jpg` in the question or in Explorer.

## 1.2.1 – 2026-10-22

### Fixed
- **AirDrop: a file held still over a chip missed it.** Windows only reports a drag while the mouse moves, so the share view closed half a second after you stopped moving, and the file then landed on the bare notch ("Drop it on a person or on Code"). The view now stays open until the drag really leaves the notch or ends. Found by a live test that drags a real file onto the notch.
- The chips are no longer rebuilt during a drag when no one new showed up, so the chip under a still mouse stays highlighted.

### Added
- End-to-end test for the other direction: a running notch sends (a file dropped on *Code*), the test receives it through the relay and checks name and bytes.
- `--diag` logs drags and drops on the notch.

## 1.2.0 – 2026-10-22

### Added
- **AirDrop.** Drag files onto the notch and drop them on a person nearby (other WinNotch users on the same network) or on **Code** to send them anywhere over the internet. The receiver's notch shows who sends what and asks *Accept* / *Decline*; files are saved to Downloads.
  - Same network: discovery over UDP broadcast (port 47811), transfer over TCP, a fresh ECDH key per transfer (protects against eavesdropping; peers are not authenticated).
  - Anywhere: a one-time code (`WN-…`, 128 bits) that is copied for you; when your friend copies it, their notch offers to receive. Data goes through the public magic-wormhole transit relay, which only sees encrypted bytes.
  - AES-256-GCM for everything; tampered or replayed data is rejected. File names are sanitised, existing files are never overwritten, cancelled transfers leave nothing behind, received files carry Windows' "downloaded from the internet" mark.
  - Right-click › *AirDrop* switches it all off; *Receive with copied code* starts a receive by hand.
- **Volume pop-out:** the notch shows the system volume when it changes (keys, taskbar, other apps). Event driven, and it follows a change of output device.
- **Shuffle and repeat** buttons in the open notch (when the player supports them), the album name next to the app, and clicking the cover or title brings the player to the front.
- **Test project** (`dotnet test WinNotch.Tests`) with 41 tests – codes, encryption, transfer protocol, malicious senders, network discovery and real relay transfers – plus an end-to-end test that sends a file to a running notch (runs only on request).

### Fixed (found by the end-to-end test before release)
- Accepting a file sent with a code failed with a threading error: the question was shown from a worker thread.

### Changed
- New app icon: a clean blue–violet tile with the notch.
- Version 1.2.0.

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

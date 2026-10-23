# Changelog

## Unreleased

### Changed
- A blue circular download icon appears immediately after the **Updates** text in the right-click menu when a new version is available.

## 1.5.8 – 2026-10-23

### Changed
- Bluetooth battery scans run away from the UI thread so device enumeration cannot stall the notch; overlapping scans are skipped.
- The open notch updates its clock and volume visuals only when their displayed values change, reducing unnecessary redraws.

## 1.5.7 – 2026-10-23

### Changed
- The expanded notch is wider, and device badges wrap onto another line when needed so the Bluetooth and audio controls stay visible.
- Automatic update checks run every two hours instead of every six hours.
- Published self-contained single-file builds use compression to reduce download and disk size.
- The Bluetooth menu no longer shows a redundant *Add a device…* entry; *Bluetooth settings* remains available for pairing new devices.

## 1.5.6 – 2026-10-23

### Added
- **Bluetooth in the notch.** A Bluetooth button next to the output device button opens a list: a switch that turns Bluetooth on and off (like the one in Settings), your paired devices with a tick at the connected ones and their battery level, then *Add a device…* and *Bluetooth settings*. Picking headphones, earbuds or a speaker connects or disconnects them – music and calls alike – through the same request the Bluetooth settings page sends to the audio driver; the batteries and the "connected" pop-out follow once Windows has connected. Mice, keyboards and phones are listed but greyed out: they connect by themselves, and Windows has no way for an app to do it. The button is dimmed while Bluetooth is off and only there on a PC with Bluetooth.

## 1.5.5 – 2026-10-23

### Added
- **Output device.** A new button next to the volume in the open notch lists your speakers, headphones and headsets, with a tick at the one playing now and an icon for each kind (headphones, headset, speakers, a monitor over HDMI). Picking one makes it the default for music, games and calls alike, like choosing it in Windows' Sound settings. The button is only there when there's more than one device to pick.
- **Friends online.** Dragging files onto the notch now asks your friends' notches whether Holm is running; those that are answer within a second or two and get a green dot on their avatar ("One friend is online."). Each friend is asked at most once a minute and answers at most every 10 seconds – every note counts against ntfy.sh's daily limit – and counts as online for two minutes. Only friends get an answer. Older versions ignore these notes.
- **VPN indicator.** While a VPN is connected, a small lock shows in the idle notch and a green lock (with the VPN's name as tooltip) next to the batteries in the open notch; connecting and disconnecting pop out "VPN connected · NordVPN" / "VPN disconnected". It recognises Windows' own VPN connections (with the name you gave them) and the tunnel adapters of WireGuard, OpenVPN, NordVPN, Proton VPN, Mullvad, ExpressVPN, Surfshark, Cisco AnyConnect, GlobalProtect, FortiClient and others, and ignores Hyper-V, WSL, Teredo and the like. Windows reports address changes, so nothing is polled. Right-click › *VPN indicator* turns it off.

### Fixed
- Debug builds' `--snap` (the README pictures) could show real messages that had come in before it took over, and could save its pose – equalizer off, normal sizes – into your settings when the friends inbox caught up. It now clears the messages first and never saves settings.

## 1.5.4 – 2026-10-23

### Changed
- Friend requests now wait for the recipient to accept or decline before either list is updated. Accepting synchronizes both lists, including when an older one-sided friend entry exists; cancelling closes the other person's pending request and undoes an addition if the accept and cancellation crossed.
- The friend removal notice now appears as one line, so its text no longer overlaps.
- Holm now fully exits if its old process remains alive 10 seconds after an update; normally it exits in about 2 seconds. Debug builds also expose switches for checking the shutdown behavior.
- The README now shows the updated Holm logo and describes the current friend request flow.

### Fixed
- **After an update, the old Holm could stay alive without a window.** On 23 October a quit Holm 1.5.1 was found still running 35 minutes after its update, with no notch left; the updater gave up waiting for it after 90 seconds and kept the old version (a second Holm then updated). Now, if the process still exists 10 seconds after Holm quit, it notes that in `error.log` and ends itself – normally it is gone after about 2 seconds and this never fires.
- Starting an update also restarted Beam and the friends inbox for a moment: `Shutdown()` only queues the quit, and the code after it switched sharing back on before the app closed. The update now marks the app as closing first.
- Debug builds take `--quit-after N` and `--linger` to check that the process really ends.

## 1.5.3 – 2026-10-23

### Changed
- Removing a friend now sends a sealed removal notice so their Holm removes you from its list too. If the notice cannot be delivered, Holm says so.
- Holm now removes duplicate or invalid saved friend entries on startup, keeping the latest entry for each public key.
- The application icon now uses the updated Holm logo.

## 1.5.2 – 2026-10-23

- File sending checks that the recipient's notch is open before offering files; no answer within 15 seconds reports that they're offline or Holm is closed.
- Incoming file offers play a sound, and cancelling while the recipient's accept prompt is open now tells them the sender cancelled.
- Adding a friend you already have now shows that they're already in your friends list and skips adding or notifying them again.

### Fixed
- **The 5-second check for messages read everything every time.** Since 1.5.0 Holm looks for new messages every 5 seconds in case Windows' folder events go missing, and every look asked the notification center for all its notifications (43 here) and copied the notification database for the badges. Idle, that came to about 1.2 % CPU on an 8-thread laptop – more than 20 times 1.4.0 – and 20–30 MB more working set. Now a timer compares a marker that Windows rewrites with every change to the database (a fifth of a millisecond) every 2 seconds and reads only when it moved; the notification center, the expensive part, is only asked when a notification arrived, changed or went, not for a new badge. New messages show up within 2–4 seconds, as fast as before (measured with silent test notifications).
- **The equalizer kept running after the music stopped.** Its bars stood still, but their endless animations went on ticking in the background until .NET happened to clean up, often for minutes – as much CPU as playing (about 0.2 % instead of 0.03 % on an 8-thread laptop). They're stopped properly now. The same happened whenever the equalizer paused for a pop-out or a size change while music played.

### Changed
- **Less work in the background.** Beam's announcement once a minute no longer lists every network adapter each time (the addresses are kept until Windows reports a change), and Holm runs its precompiled code as it is instead of compiling busy methods again in the background (.NET's tiered compilation is off; that also saves the 3–5 MB steps it caused). Idle, Holm now needs about 0.02–0.03 % CPU: half of 1.4.0 (0.055–0.095 % in the same runs) and a fortieth of 1.5.1.
- **Memory goes back to Windows more often:** also a few seconds after the music stops, and once after the update check shortly after start, which otherwise brought back about 25 MB right after the first hand-back. After music, Holm now sits at about 18 MB in Task Manager and 48 MB working set instead of 26 and 75 MB.
- **Honest numbers in the README.** The working set is only 10–16 MB for a moment after Holm hands its memory back to Windows. Idle it settles at about 20–35 MB in Task Manager (55–80 MB working set), while music plays at about 45–55 MB (95–120 MB).
- Tests for the database marker (it moves with every write, also after the log starts over) and for the toasts' fingerprint (a badge leaves it alone; a new, removed, re-timed or replaced notification changes it; no text ends up in it); that stopping music really stops the equalizer's clocks and hands memory back, and that a paused Spotify doesn't.

## 1.5.1 – 2026-10-22

### Changed
- **WinNotch is now called Holm** – a holm is a small island, and many other projects already use the name WinNotch. The app, its window, the menus, the release files (`Holm-…-win-x64.zip`) and the repository (github.com/ademerci/Holm) carry the new name.
  - Your settings carry over: on its first start Holm copies them from `%AppData%\WinNotch` to `%AppData%\Holm` and moves the *Start with Windows* entry over, so the old app no longer starts as well.
  - Beam keeps working with friends who still run WinNotch: codes, friend codes and the encryption labels are unchanged.
  - WinNotch 1.5.0 can't update itself to Holm (the release has a new name); download it once from the release page.
- **New icon:** a small island with a palm under the notch, on a sea-coloured tile.
- **Teams is no longer part of messages.** Its notifications and its taskbar number stay out of the notch; WhatsApp, Discord, Outlook, Telegram, Signal and Slack remain.

### Added
- Unread messages are grouped by app, with a separate row and badge count for each app.

### Fixed
- Discord messages stay available in the expanded notch after their Windows toast expires, and Discord uses its installed app icon.
- Starting Holm while it is already running now displays an error and prevents a second instance from opening.

## 1.5.0 – 2026-10-22

### Fixed
- **Every ping from a paused Spotify cost a full refresh.** Spotify re-sends its timeline about every 4.5 seconds, also when nothing plays, and WinNotch answered every one with a full round trip to the player and a pass over the notch. Now a timeline on its own is only checked against the position the notch already shows, and an update only reaches the notch when something you can see changed: 1 update in 45 seconds of Spotify playing instead of 11. Paused, CPU went from about 0.05 % to 0.03 % and memory stays about 3 MB lower. Most of the climb after WinNotch trims its memory (to about 45 MB within a minute) comes from the checks it runs every minute, not from Spotify.
- The open notch made a new cover brush for every update. The compact and the open notch now share one per cover.
- Tests that repeated updates aren't passed on and keep their cover, backdrop and colour brushes.

### Added
- **Updates in the app:** automatic checks shortly after startup and every six hours, a manual check in the right-click menu, and an option to download a new stable release and restart in place. Settings and friends are preserved; a failed startup restores the previous executable.
- Regression tests for release checks, verified downloads, update menu states, replacement and recovery.
- New message alerts are checked every five seconds as a fallback when Windows misses a notification database change event.

### Fixed
- New messages can reappear after opening Teams or WhatsApp even when the app's unread conversation count stays unchanged. WinNotch tracks fresh Windows badge updates and refreshed notifications that reuse the same id.
- **Missing message badges and pop-outs:** numeric badge fallback now covers WhatsApp and all supported messengers, including when reading Windows notifications fails. Existing unread counts appear at startup; new increases pop out. An unchanged zero Teams badge no longer hides a new notification, and opening one app leaves the others' unread counts alone.
- New messages can trigger an alert when an app reuses a Windows notification id or keeps the same unread conversation count.

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

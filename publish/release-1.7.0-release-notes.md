Holm 1.7.0 turns the quiet notch into today at a glance and adds twenty features: notes, reminders in plain words, a calculator, a clipboard history, a color picker, text from screenshots, volume per app, weather, quiet hours and more, plus waves, folders, text and "send later" for Beam.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.7.0-win-x64.zip** | Everyone. Unzip and run `Holm.exe`, nothing to install. |
| Holm-1.7.0-win-x64-dotnet10.exe | Smaller single exe if you already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

> **“Windows protected your PC”?** The app is not code-signed, so SmartScreen warns on first launch. Click **More info → Run anyway**. Windows Firewall may also ask once – allow it if friends on your network should find you for Beam.

### Added
- **Today at a glance.** The idle notch shows the time (or the weather) and, on its right, the one thing that matters most: the next reminder within 12 hours, else what's on the shelf. Open without music, the notch is a card for today: time and date, the weather, the next reminder and your note (each opens its tool), and chips for the calculator, the color picker, the clipboard and the mixer. The player and the card cross-fade when music starts or stops; with *Animations* off it's instant.
- **Tools in the open notch.** A tool bar under the player opens a page in place of home; click it again to go back. The notch takes the keyboard only while you type into one of its fields, and gives it back as soon as you click elsewhere, press Esc or it closes.
- **Notes.** A small note in the open notch, saved as you type and kept after a restart. It stays on this PC.
- **Reminders in plain words.** "20 min laundry", "14:30 dentist", "tomorrow 9:00 call", "1h30m tea" (German works too: "morgen 9 Uhr"). On time the notch rings with *+10 min* and *OK*; reminders survive a restart, and one missed while the PC was off or asleep shows as soon as it's back.
- **Calculator.** Type a sum, the result is there at once; click it or press Enter to copy. + − × ÷ ^ %, brackets, comma or point, sqrt, sin, cos, tan, ln, log, abs, pi and e; "50 + 15%" adds 15 % of 50, as on any calculator. Its own small parser: nothing typed is run as code.
- **Clipboard history.** The last 20 texts and pictures you copied; a click puts one back. What password managers mark as private (ExcludeClipboardContentFromMonitorProcessing, Clipboard Viewer Ignore, or kept out of Windows' own history) is never read or kept. The list lives in memory only, pictures in a temp folder emptied when Holm starts and quits; nothing is saved, logged or sent.
- **Copied passwords are wiped.** A password copied from a password manager leaves the clipboard after 30 seconds, but only if nothing else was copied since (Holm compares the clipboard's sequence number and never reads the password).
- **Color picker.** The eyedropper closes the notch; the next click anywhere copies that pixel's hex code, and the notch shows the color. A magnifier follows the cursor; Esc or a right click cancels. There is no overlay, so the color read is exactly what the screen shows.
- **Text from screenshots.** The screenshot pop-out has *Copy text*: Windows' own OCR reads the picture on this PC and the text goes on the clipboard. The button shows when Windows has an OCR language installed.
- **Volume per app.** A mixer page with a slider for each app that plays sound (all of an app's sessions under one slider); its icon mutes it. It follows Windows' audio session events while the page is open.
- **Sleep timer.** The moon in the open player stops the music after 15, 30 or 60 minutes (also in the right-click menu).
- **Pause for calls.** Music pauses when a call starts and plays on when it ends, if Holm paused it.
- **Weather.** Temperature and a symbol in the idle notch, the sky and the next hour's rain ("Rain in 20 min · 0.6 mm") in the open one. From Open-Meteo, without a key, for the city you type in the open notch, and asked at most every 30 minutes; only the city name and its coordinates leave the PC.
- **Quiet hours.** From one time of day to another (22:00 to 07:00 by default) messages don't pop out; they still count on the badge, and the open notch says it's quiet. Apps or people you choose still pop out. Off by default.
- **Caps Lock.** The notch says when Caps Lock goes on or off. A low-level keyboard hook compares each key with Caps Lock only; no key is stored or logged.
- **Low disk space.** One warning when the system drive gets under 10 % or 10 GB free, with *Clean up* (Storage Sense); again only after it had room in between.
- **USB drives.** A USB stick or SD card pops out with *Open* and *Eject*, and stays in the open notch while plugged in. *Eject* works like Safely Remove Hardware and says so when a program still uses the drive.
- **Internet connection.** "No internet" the moment it drops, "Back online · after 3 min" when it returns, from Windows' Network List Manager events. Drops shorter than two seconds and those around sleep don't count.
- **Wave at a friend.** Friends › *Wave at* pops up on their notch with *Wave back*. At most once per friend every 30 seconds each way.
- **Send folders.** A folder dropped on a person is packed as a ZIP, sent, and the ZIP deleted afterwards.
- **Beam text.** Send copied text to a friend, someone nearby, your phone or a code (*Beam text* in the menu, or the arrow next to a text in the clipboard history). It lands on their clipboard.
- **Send later.** When a friend's Holm isn't running, what you dropped on them waits and goes once their Holm is back; *Don't send* takes it back. Holm asks every 15 minutes for two hours, then hourly, and leaves a "waiting" note every six hours, which a Holm 1.7 answers as soon as it starts. After a week it gives up.

### Changed
- Every new feature has its own switch in the right-click menu, and settings files from older versions load as before.
- New friend notes (*wave*, *waiting*) and the text mark on beamed files are ignored by older Holm versions: they leave unknown notes unopened and save a beamed text as an ordinary file in Downloads.
- The right-click menu's *Animate equalizer* is now *Animations*; it also covers the new cross-fades.

### Fixed
- The open notch could come out too short or too tall right after its content changed (a longer note, new rows), because it was measured before the layout caught up.

## Tested

727 automated tests. 680 passed on the Release build under Wine; the other 47 need Windows parts Wine doesn't have (ECDH in bcrypt, audio devices, the notification database, the update installer's process handling), and the friend-note ones among them passed on .NET with real cryptography. Hardware-bound features (USB eject, Caps Lock hook, OCR, color picker, per-app volume, network events) were checked as far as Wine allows and still need a run on Windows.

Full list in the [CHANGELOG](https://github.com/ademerci/Holm/blob/v1.7.0/CHANGELOG.md). Open source under the MIT license.

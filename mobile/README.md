# Holm for Android

A Flutter app (Android 15+) that works with Holm on your laptop. The iOS project (iOS 18+) is there, but has not been built or tested; the Dynamic Notch is Android only. It uses Holm's dark hub look: panels, cards, the blue accent and the blue-to-violet chat bubbles. It has five pages:

| Page | What it does | How it reaches the laptop |
| --- | --- | --- |
| **Devices** | Beams files and text both ways. **Your devices** lists the PCs signed in to your Holm account, on any network; **On this Wi-Fi** finds Holm nearby. Received files can be opened, shared or deleted. | Anywhere: a `files` note in your own inbox, then the transfer through the relay (`RelayLink.cs`, `ShareCode.cs`), as Holm sends to friends. Nearby: Holm's LAN protocol (`NearbyService.cs`, `FileTransfer.cs`, `SecureChannel.cs`). |
| **Chat** | Friends, friend requests, invite codes (`HOLM-XXXXX-XXXXX`), messages, reactions and waves. Online status comes from ping and here. | Sealed friend notes in the inbox on `relay.holmnotch.ch`, with `ntfy.sh` as the fallback, as in `Friends.cs` and `FriendInvite.cs`. |
| **Clipboard** | Clipboard history. Sends the clipboard or any entry to a device. With **Sync with laptop** on, what you copied goes over when you open the app, and texts from the laptop are copied straight away. Works nearby and, signed in, anywhere. | A beamed text (`"Text": true` in the offer). Holm puts it on the laptop's clipboard. |
| **Notch** | The Dynamic Notch on Android (see below): switching it on, fitting it to the camera with a live preview, what it shows. | — |
| **Notes** | One note, as Holm's notepad in the notch (up to 4,000 characters, kept as you type). You can copy it, share it, or send it to the laptop's clipboard. | A beamed text. |

On the same Wi-Fi the phone speaks the protocols Holm already has and shows up under Beam like any other Holm.

## The Dynamic Notch (Android)

A notch around the camera, like Holm's on the desktop. It is invisible until something happens; then it shows up to two live activities at once (one, or two with the second as a bubble beside it). Tapping it opens it, swiping up or tapping elsewhere closes it. Closed it is always black, like the camera; open it uses Holm's look (the music's cover wash) or a **Nothing** look (black and white, dot-matrix lettering drawn dot by dot, a red dot).

| Activity | From | Stays |
| --- | --- | --- |
| Music, with play/pause, skip and seek, coloured from the cover as on the desktop (`CoverMath`) | The playing media session (Spotify or any player) | While it plays, a minute after pausing |
| New notifications, with the app's icon and a ring in its colour for well-known apps | Notifications | 5 s |
| Timers and stopwatches | Ongoing clock notifications with a chronometer | While running |
| Downloads, with progress, then *Downloaded* | Ongoing notifications with progress | Until done |
| Holm: files and texts from your PC, offers to accept or decline in the notch | The app's own events (it keeps running in the background) | While sending; 4 s when done |
| Holm chat | New messages while the app is closed | 5 s |
| Charging, Wi-Fi, Bluetooth, VPN (Mullvad with a little mole popping out of its hole) | System broadcasts and network callbacks | 3–4 s |
| Quick Share, Live Updates (Android 16 promoted notifications), reminders | Notifications | While ongoing / 5 s |

How it is built:

- `NotchService.kt` is an **accessibility service** that only draws: an accessibility overlay may lie over the status bar and the display cutout. It reads no screen content and takes no actions. The overlay window is just as big as the notch needs and untouchable (1×1) while nothing shows.
- The notch is drawn by its own Flutter engine (`notchMain` in `lib/main.dart`, `lib/notch/`), from the same `FlutterEngineGroup` as the app. The app's engine is cached and kept while the service runs, so Holm keeps following its inbox and nearby devices with the app closed.
- `NotchListener.kt` (notification access) describes notifications and the media session; `lib/notch/activities.dart` decides what each one becomes.
- The shape moves on the desktop's springs (`Spring.cs` → `lib/notch/spring.dart`, the response and damping of `NotchWindow.MoveShapeTo`).
- Settings (size, position and corner radius closed and open, one or two activities, the look, which activities) live in the app and reach the notch at once; **On screen** shows sample activities in the real notch while you fit it.

**Hide Android's pop-ups** (Notch page) turns Android's own heads-up pop-ups off while the notch runs, so a notification shows once, in the notch; it stays in the notification shade. Android only lets an app change that setting (`heads_up_notifications_enabled`) with `WRITE_SECURE_SETTINGS`, which no app can ask for: it is granted once from a computer with `adb shell pm grant ch.holmnotch.holm_mobile android.permission.WRITE_SECURE_SETTINGS`. Holm switches the pop-ups back on when the notch stops, and only if it was Holm that switched them off.

Without a computer, **Unlock with Shizuku** does the same: with [Shizuku](https://shizuku.rikka.app) running, Holm asks it once to run that `pm grant` (`ShizukuGrant.kt`); the permission stays afterwards.

Open, each corner of the notch can have its own radius (*Each corner on its own*). Swipe the notch sideways to send a notification away (it stays in the shade); the bell in an open notification stops that app showing in the notch, and *Notifications* on the Notch page lists those apps and the switch for Android's own notices (battery saver, USB, system updates), which are off by default.

### New in 1.2

- **Reply in the notch.** Messages with a reply button get *Reply* in the open notch: type, send, done. (Android 15 hides the content and buttons of notifications it marks as sensitive from every app but its own; those show with the app's name only.)
- **Swipe directions.** To the left a notification leaves the notch and stays in the shade; to the right it is cleared from the shade too.
- **Quiet.** *With DND* (the default) or *At times*: then only calls, timers and alarms show up.
- **Calls.** A ringing call opens the notch with *Answer* and *Decline*; while you talk it shows the time, *Mute* and *Hang up*. Answering and hanging up need *Answer calls in the notch* (the `ANSWER_PHONE_CALLS` permission): Android gives listeners call notifications without their buttons.
- **Volume** in the notch instead of Android's slider: the service asks for the volume keys (`flagRequestFilterKeyEvents`) and also follows Android's volume broadcast, so the notch shows any change.
- **Screenshots to your PC.** A new screenshot (with *Screenshots to your PC* allowed) is offered in the notch; *Send* sends it to the device you last sent something to.
- **Clipboard from your PC.** Text from the PC that arrives while Holm is in the background shows in the notch, with *Copy again*.
- **Navigation** from Google Maps (the next turn and its arrow), **camera and microphone in use** (a dot and the app), **headphone battery** when they connect.
- **Gestures.** Double tap and long press on the notch: torch, play/pause, screenshot, open Holm, new note, open the notch.
- **Overview.** With nothing showing, a tap on the camera opens the time, the batteries, the next alarm and event (with calendar access) and your PCs.
- **App colours** and Holm's **24 designs** for the open notch (`lib/notch/designs.dart`, from `Holm.Core/Design.cs`).
- **Also on your PC.** Signed in to your Holm account, the phone tells the account's PCs about notifications, calls, new songs and timers (a `phone` note in the account's own inbox, end-to-end encrypted); their notch pops it out. Off with *Also on your PC*.

### New since 1.2.2

- **Share menu.** Holm is in Android's share menu: text, links and files go to one of your devices.
- **Compact notifications** (*Kompakte Benachrichtigungen*): only as tall as the closed notch, the sender left of the camera, the message right of it.
- **Long text** rests for 1.5 s, then slides back and forth to show all of it (`lib/notch/marquee.dart`).
- **Volume with a finger.** Drag along the volume pill; hold a volume key and it keeps going; open, the notch has sliders for media, ringtone, notifications, alarm and calls.
- **Earbuds** (*Earbuds*): while connected, small in a bubble beside the camera (alone, in the notch itself; with two other activities, not at all). Tap it and they come out of their case. Pixel Buds Pro 2 (pick their colour on the Notch page), AirPods 4 and Nothing's earbuds have a 3D picture: frames rendered from their models, side by side in one picture (`assets/buds/*_out.webp`), and the buds alone for the bubble (`*_buds.webp`). Each bud's and the case's battery come from Google Play services' Fast Pair card, whose text the listener reads from its view (`NotchListener.sendBudsBattery`); that card no longer pops up.
- **Overview** has a settings button; an open notification has a **snooze** (the app stays out for 5 minutes) beside the bell.
- **Swipe the open notch** sideways to close it (a notification goes with it). The notch **steps aside** while the notification shade or quick settings are pulled down (it watches the system's windows: `typeWindowsChanged`, `canRetrieveWindowContent`).
- **From your PC:** the PC's phone tab can close a notification here (a `phone` note with `Y` = `dismiss` and the notification's key) and play, pause or skip the music (`Y` = `control`).
- Covers given only as a link (Spotify) are fetched; the song bar takes taps and drags; a screen recording shows as a recording, not a stopwatch.

## Updates

Holm for Android updates itself from Holm's update server (`https://62.83.85.236:8473`, nginx, self-signed certificate pinned in `lib/update.dart`). It checks at start and every six hours; a newer `versionCode` in `latest.json` shows a notification and a banner with **Update**, which downloads the APK, checks its SHA-256 and installs it with Android's PackageInstaller (Android asks once to allow Holm to install apps, then to confirm each update).

Settings (the gear) shows the version, **Check** for updates now, and **Test versions**, which also takes `beta.json` when it is newer. After an update, Holm shows once what's new in it.

To publish: raise `version` in `pubspec.yaml` (the number after `+` must grow) and run

```powershell
flutter build apk --release
./tool/publish-update.ps1 -Notes "What's new"
./tool/publish-update.ps1 -Channel beta -Notes "For testers"
```

**Signing.** From 1.2 on, Holm is signed with its own release key, kept outside the repository in `%LOCALAPPDATA%\HolmDev\keys` (`holm-release.jks`, `key.properties`, `lineage.bin`). `tool/sign-release.ps1` signs with a key rotation lineage from the debug key 1.0 and 1.1 were signed with (APK Signature Scheme v3.1), so phones with 1.1 accept 1.2 as an update. Keep a copy of that folder: without it no update can be published that installed apps accept.

To use it: Notch page › *Dynamic Notch* (turn on *Holm Dynamic Notch* under Accessibility) and *Notifications and music* (notification access). Bluetooth device names need the Bluetooth permission. Apps that use an accessibility service for something other than accessibility have to explain it for Google Play.

## Your Holm account: your devices anywhere

Sign in on the phone (Settings › Holm account, or **Sign in** on Devices) with the email and password you use in Holm on the PC. As on the desktop (`Holm.Core/Account`), the password never leaves the phone: PBKDF2 (600,000 rounds) turns it into a sign-in secret for Firebase and a vault key that opens the account's data key. The phone then reads the account's friends identity, friends and profile name. It only reads; the PCs keep writing and merging the account.

With the same identity, the PCs and the phone share one inbox on the relay. A note sealed by that identity for itself comes from one of your own devices (`Holm.Core/Sharing/OwnDevices.cs`):

- `device` (`D` = the device's id, `H` = "who else is there?") says a device runs. The phone sends it when it opens; PCs answer, and send it when Beam starts and when the share view opens.
- `files` with `D` (from) and `N` (for) offers files or a text to one device. Both then meet on the transit relay with the secret `S`.

Each device skips its own notes (`D` is its own id). Holm on the PC shows your devices in the share view and accepts their files and texts (this needed the small change in `FriendsService` and `NotchWindow.Beam.cs`; older Holm versions ignore these notes). The phone gets notes while it is open; a PC sending to it when it is closed hears *isn't available*.

## Code

```text
mobile/
├── lib/
│   ├── main.dart            # App frame, bottom tool row, offer dialog, settings
│   ├── app_state.dart       # Everything the app knows and does (ChangeNotifier), saved to holm.json
│   ├── notch/               # The Dynamic Notch: activities, springs, the notch view, its engine's entry, the link to the app
│   ├── net/                 # Protocols, plain Dart (no Flutter), byte-compatible with Holm.Core
│   │   ├── crypto.dart          # P-256 SPKI, DeriveKeyFromHash, AES-GCM, HKDF (pointycastle)
│   │   ├── secure_channel.dart  # ECDH handshake + AES-GCM frames ("WinNotch LAN v1")
│   │   ├── nearby.dart          # UDP discovery on port 47811, TCP server
│   │   ├── transfer.dart        # Offer / answer / data / done / received
│   │   ├── relay.dart           # Transit relay and share secrets (RelayLink.cs, ShareCode.cs)
│   │   ├── account.dart         # Holm account: keys, Firebase sign-in, the sealed vault (read-only)
│   │   └── friends.dart         # Friend codes, sealed notes, inbox, invite codes
│   └── ui/                  # Pages and Holm's colors (theme.dart mirrors Design.cs Classic)
└── test/                    # Values made by Holm.Core (protocols, account keys, vault), transfers over loopback
```

New features (Dynamic Island / Live Activities, widgets, a share target) can live in their own folder under `lib/` and use `AppState` without touching the protocols.

## Build and run

You need Flutter 3.50 or newer (stable) and, for Android, an Android SDK with platform 35 or newer and JDK 21.

```powershell
cd mobile
flutter pub get
flutter test
flutter run            # a connected phone or a running emulator
flutter build apk      # Android
flutter build ipa      # iOS, on a Mac with Xcode
```

### Trying it with Holm on the same PC (Android emulator)

The emulator sits behind its own NAT, so broadcasts don't reach the PC. On **Devices**, tap **By address** and enter `10.0.2.2` (the PC as seen from the emulator). Holm on the PC answers with its port, and the phone can send to it. Sending from the PC to the emulator does not work this way. Use a real phone for that.

## Notes for the platforms

- **Android:** the app holds a Wi-Fi multicast lock while it runs, because many phones drop broadcast packets otherwise. Android lets an app read the clipboard only while it is in front, so clipboard sync looks when the app opens.
- **iOS:** the first search asks for *Local Network* access (`NSLocalNetworkUsageDescription`). Sending UDP broadcasts on iOS needs Apple's `com.apple.developer.networking.multicast` entitlement, which has to be requested from Apple for the App Store build. Without it, use **By address**. iOS only lets the app read the clipboard when the person pastes or allows it.
- On the laptop, Holm has to be running with Beam on. The firewall may ask once to let it reach the network.

## Not in this version

- Friends' file and stream offers over the internet relay (`files` / `stream` notes from friends). Your own devices' files do come over the internet.
- Writing to the account (friends added on the phone stay on the phone), and creating an account (do that in Holm on the PC).
- Notifications while the app is closed: the phone reads its inbox while it is open.
- GIF search and profile pictures in chat.
- The desktop's notes, which sync through the Holm account (Firebase). The phone keeps its own notes.

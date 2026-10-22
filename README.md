<img src="docs/icon.png" width="72" align="left" alt="WinNotch icon">

# WinNotch

A Dynamic Island–style notch for Windows. It sits at the top of your screen, shows what's playing with full controls, tints itself in the colours of the album cover, sends files to your friends AirDrop-style and keeps an eye on your laptop and Bluetooth headphone battery – while using almost no resources.

<p align="center">
  <img src="docs/expanded.png" width="480" alt="Expanded notch with media controls, shuffle and repeat, battery levels">
</p>
<p align="center">
  <img src="docs/compact.png" width="480" alt="Compact notch while music plays">
  <img src="docs/volume.png" width="480" alt="Volume pop-out">
  <img src="docs/peek.png" width="480" alt="Pop-out when headphones connect">
</p>

## Features

- **Media controls** for anything that talks to Windows' media controls – Spotify, YouTube in Chrome or Edge, VLC, … Cover, title, album, seek bar, play/pause, skip, **shuffle and repeat**, volume. Click the cover to bring the player to the front.
- **Album colours** – the notch is washed in a blurred copy of the cover and crossfades when the track changes.
- **Volume pop-out** – change the volume with your keys or the taskbar and the notch shows it, like a phone does.
- **AirDrop** – drop files on the notch and send them to a friend with WinNotch, on the same network or anywhere over the internet (see below).
- **Battery levels** for your laptop and Bluetooth devices (the same values Windows Settings shows).
- **Live activities** – short pop-outs when headphones connect, the charger is plugged in or the battery runs low.
- **AI button** – one click opens Claude, Gemini or ChatGPT: the desktop app if you have it (also Store apps and web apps installed from Edge or Chrome), the website otherwise. Or pick any app you like.
- **Teams messages** – a new message pops out of the notch with who wrote and the first line, and a purple badge counts what you haven't seen yet. Opening Teams (or clicking the message in the notch) clears it.
- **Stays out of your way** – hides during fullscreen games and videos, never steals focus, works on any monitor.
- **Right-click menu** – animated equalizer, minimize when idle, start with Windows, AI button (Claude, Gemini, ChatGPT, choose app…, off), Teams messages, AirDrop, receive with a copied code, pick a screen, quit.

## AirDrop

<p align="center">
  <img src="docs/airdrop-share.png" width="480" alt="Dropping a file on the notch: pick a person or Code">
  <img src="docs/airdrop-ask.png" width="480" alt="The receiver's notch asks before anything is saved">
</p>

Drag files onto the notch. It opens and shows:

- **People nearby** – everyone running WinNotch on the same network. Drop the files on a person and their notch asks *Accept* or *Decline*.
- **Code** – works anywhere, also on networks that don't let devices talk to each other (school, office, hotspots). Drop the files on *Code* and a code like `WN-K7Q2A-…` is copied for you. Paste it to your friend in any chat; as soon as they copy it, their notch offers to receive the file. Right-click › *Receive with copied code* does the same.

Received files land in your **Downloads** folder.

How it stays safe:

- **Nothing arrives without a yes.** The receiver sees who is sending what (names and sizes) and has to accept; no answer within a minute counts as no.
- **Encrypted.** With a code, the key comes from the code itself, so only the person you gave the code to can read the file. On the network both notches agree on a fresh key for every transfer (ECDH); that stops anyone from listening in, but the notches don't verify each other, so someone on the same network could pretend to be a notch – use *Code* on networks you don't trust. Everything is sent with AES-256-GCM, so changed or replayed data is rejected.
- **Through the relay, only scrambled bytes.** Code transfers go through the public *transit relay* of the open-source [magic-wormhole](https://github.com/magic-wormhole/magic-wormhole) project. It only pairs the two sides and passes encrypted data along – it never sees the code, the key, file names or content. A code works once and expires after 15 minutes.
- **Files stay where they belong.** File names are cleaned up, so nothing can be written outside Downloads; existing files are never overwritten (you get `photo (1).jpg`); a cancelled transfer leaves no half file behind.
- **Marked as downloaded.** Received files get Windows' "from the internet" mark, so Windows warns before running a received program, just like with a browser download.
- To be found on the network, WinNotch listens on UDP port 47811 and a TCP port, so Windows Firewall may ask once. Turn AirDrop off in the right-click menu if you don't want any of it.

## Lightweight

Native WPF on .NET 10 and event driven: it reacts when Windows reports a change instead of polling.

| State | CPU | RAM |
| --- | --- | --- |
| Idle | ~0.05 % | ~10–16 MB |
| Music playing, equalizer animated | ~0.3–0.5 % | ~10–20 MB |

Measured on an 8-thread laptop. Turning off the equalizer animation brings playback down to idle levels.

**Teams messages** are event driven too. Windows only tells packaged (Store) apps when a notification arrives, so WinNotch watches the folder of Windows' notification database instead and reads the notifications only when it changes. In between it costs nothing; a check once a minute is the safety net.

## Teams messages

WinNotch reads Teams' notifications from the Windows notification center, so it needs **notification access**. It asks for it on the first start; if Windows says no, the switch is in *Settings › Privacy & security › Notifications* (choosing *Teams messages* in the right-click menu takes you there). 

**Teams has to send its notifications to Windows.** If its pop-ups appear in Teams' own style and not in the Windows notification center, WinNotch can't see them. In the new Teams, open *Settings › Notifications and activity* and set *Notification style* to **Windows**; also check that Microsoft Teams is allowed in *Windows Settings › System › Notifications*.

WinNotch never removes or changes your notifications, and the message text is never written to its logs.

Microsoft doesn't document reading notifications from an app that isn't installed as a package. Other unpackaged apps do it on current Windows 11 builds; where Windows refuses, the menu entry says so and is greyed out.

## Requirements

Windows 10 (2004 or newer) or Windows 11, x64.

## Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet publish WinNotch -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

This gives you `publish/WinNotch.exe`, which needs the .NET 10 Desktop Runtime. For a single exe that runs anywhere without installing .NET, use `--self-contained true -p:IncludeNativeLibrariesForSelfExtract=true` instead.

## Tests

```bash
dotnet test WinNotch.Tests
```

Covers the AirDrop codes, the encryption (tampering, wrong keys, replays), the transfer protocol (including a sender that tries `..\` paths or sends more than it offered), two notches finding each other and sending a file over the network, and real transfers through the public relay (these need an internet connection; skip them with `--filter "Category!=Online"`).

## Vibe coded

This project was vibe coded: it was built entirely by chatting with Claude (Anthropic's AI) in Claude Code, from the idea to the performance tuning. It is tested on a real machine, but expect some rough edges – issues and pull requests are welcome. 

## License

Open source under the [MIT License](LICENSE).

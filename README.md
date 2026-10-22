# WinNotch

A Dynamic Island–style notch for Windows. It sits at the top of your screen, shows what's playing with full controls, tints itself in the colours of the album cover, and keeps an eye on your laptop and Bluetooth headphone battery – while using almost no resources.

<p align="center">
  <img src="docs/expanded.png" width="480" alt="Expanded notch with media controls and battery levels">
</p>
<p align="center">
  <img src="docs/compact.png" width="480" alt="Compact notch while music plays">
  <img src="docs/peek.png" width="480" alt="Pop-out when headphones connect">
</p>

## Features

- **Media controls** for anything that talks to Windows' media controls – Spotify, YouTube in Chrome or Edge, VLC, … Cover, title, seek bar, play/pause, skip and volume.
- **Album colours** – the notch is washed in a blurred copy of the cover and crossfades when the track changes.
- **Battery levels** for your laptop and Bluetooth devices (the same values Windows Settings shows).
- **Live activities** – short pop-outs when headphones connect, the charger is plugged in or the battery runs low.
- **AI button** – one click opens Claude, Gemini or ChatGPT: the desktop app if you have it (also Store apps and web apps installed from Edge or Chrome), the website otherwise. Or pick any app you like.
- **Teams messages** – a new message pops out of the notch with who wrote and the first line, and a purple badge counts what you haven't seen yet. Opening Teams (or clicking the message in the notch) clears it.
- **Beam** – drag files onto the notch and drop them on a friend. It works on any network that reaches the internet, including guest and school Wi-Fi where computers can't see each other, and everything is end-to-end encrypted.
- **Size** – make the notch smaller or bigger.
- **Stays out of your way** – hides during fullscreen games and videos, never steals focus, works on any monitor.
- **Right-click menu** – animated equalizer, minimize when idle, start with Windows, AI button (Claude, Gemini, ChatGPT, choose app…, off), Teams messages, Beam, size, pick a screen, quit.

## Lightweight

Native WPF on .NET 10 and event driven: it reacts when Windows reports a change instead of polling.

| State | CPU | RAM |
| --- | --- | --- |
| Idle | ~0.05 % | ~6–15 MB |
| Music playing, equalizer animated | ~0.3–0.5 % | ~10–20 MB |

Measured on an 8-thread laptop. Turning off the equalizer animation brings playback down to idle levels.

One exception to "event driven": Windows only tells packaged (Store) apps when a notification arrives, so while **Teams messages** is on, WinNotch looks into the notification center every 2 seconds. It's a small local call that only reads notifications it hasn't seen yet, but it isn't free and isn't in the numbers above. Turn it off in the right-click menu if you don't use Teams.

Beam keeps one idle connection to its server open, but only once you've added a friend.

## Teams messages

WinNotch reads Teams' notifications from the Windows notification center, so it needs **notification access**. It asks for it on the first start; if Windows says no, the switch is in *Settings › Privacy & security › Notifications* (choosing *Teams messages* in the right-click menu takes you there). Teams has to show its notifications through Windows, which is the default in the new Teams. WinNotch never removes or changes your notifications, and the message text is never written to its logs.

Microsoft doesn't document reading notifications from an app that isn't installed as a package. Other unpackaged apps do it on current Windows 11 builds; where Windows refuses, the menu entry says so and is greyed out.

## Beam

Beam sends files from one WinNotch to another, like AirDrop, but between friends rather than to whoever is nearby.

1. Right-click the notch › **Beam › Copy my friend code**, and send the code to your friend (chat, mail, anything).
2. Your friend chooses **Beam › Add friend…** and pastes it. Then they do the same the other way round: **you only receive files from people you have added**.
3. Drag one or more files onto the notch; it opens into your friends. Drop the files on one of them.

Files arrive in your **Downloads** folder, even if WinNotch wasn't running when they were sent (as long as it starts within 3 hours). They are marked as downloaded from the internet, so Windows checks them like a browser download.

**How it gets through:** the files don't go directly from PC to PC, which is exactly what guest and school networks block. They go through a small relay server: [ntfy.sh](https://ntfy.sh), a free public service, unless you run your own. If a network blocks that server, Beam doesn't work there.

**Privacy:** every file is encrypted on your PC for that one friend (P-256 key agreement, AES-256-GCM) and only their WinNotch can open it. The server sees your IP address, when and how much you send, and to which inbox, but not the file, its name or your name. Your private key never leaves your PC and is stored encrypted for your Windows account.

**Limits:** on the free ntfy.sh, a file can be up to **2 MB** (about a photo or a PDF), each sender can have 20 MB waiting at a time, and files are deleted after 3 hours. For bigger files run your own [ntfy server](https://docs.ntfy.sh/install/) (15 MB by default, more if you configure it) and put its address in `%AppData%\WinNotch\settings.json` as `"BeamServer": "https://ntfy.example.com"` before you copy your friend code. If you change it later, send your friends your code again.

## Requirements

Windows 10 (2004 or newer) or Windows 11, x64.

## Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet publish WinNotch -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

This gives you `publish/WinNotch.exe`, which needs the .NET 10 Desktop Runtime. For a single exe that runs anywhere without installing .NET, use `--self-contained true -p:IncludeNativeLibrariesForSelfExtract=true` instead.

## Vibe coded

This project was vibe coded: it was built entirely by chatting with Claude (Anthropic's AI) in Claude Code, from the idea to the performance tuning. It is tested on a real machine, but expect some rough edges – issues and pull requests are welcome. 

## License

Open source under the [MIT License](LICENSE).

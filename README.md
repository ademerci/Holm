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
- **Stays out of your way** – hides during fullscreen games and videos, never steals focus, works on any monitor.
- **Right-click menu** – animated equalizer, minimize when idle, start with Windows, AI button (Claude, Gemini, ChatGPT, choose app…, off), Teams messages, pick a screen, quit.

## Lightweight

Native WPF on .NET 10 and event driven: it reacts when Windows reports a change instead of polling.

| State | CPU | RAM |
| --- | --- | --- |
| Idle | ~0.05 % | ~6–15 MB |
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

## Vibe coded

This project was vibe coded: it was built entirely by chatting with Claude (Anthropic's AI) in Claude Code, from the idea to the performance tuning. It is tested on a real machine, but expect some rough edges – issues and pull requests are welcome. 

## License

Open source under the [MIT License](LICENSE).

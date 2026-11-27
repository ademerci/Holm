<p align="center">
  <img src="docs/icon.png" width="110" height="110" alt="Holm app icon">
</p>

<h1 align="center">Holm</h1>

<p align="center"><strong>A Dynamic Island-style desktop notch for Windows and Linux.</strong></p>
<p align="center">Control playback, see what needs your attention, and share files from the top of your screen.</p>

<p align="center">
  <a href="https://github.com/ademerci/Holm/releases/latest"><img src="https://img.shields.io/badge/Download-GitHub-0969DA?style=for-the-badge&amp;logo=github" alt="Download Holm on GitHub"></a>
  <a href="https://apps.microsoft.com/detail/9NZF994ZPFMH"><img src="https://img.shields.io/badge/Get_it_on-Microsoft_Store-256F70?style=for-the-badge" alt="Get Holm on the Microsoft Store"></a>
  <a href="https://holmnotch.ch/"><img src="https://img.shields.io/badge/Website-holmnotch.ch-374151?style=for-the-badge" alt="Visit holmnotch.ch"></a>
</p>

<p align="center">
  <a href="https://github.com/ademerci/Holm/releases/latest"><img src="https://img.shields.io/github/v/release/ademerci/Holm?label=Version&amp;color=0969da" alt="Latest stable Holm version"></a>
  <a href="#install"><img src="https://img.shields.io/badge/Platform-Windows_%2B_Linux-256F70" alt="Platforms: Windows and Linux"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-327247" alt="MIT License"></a>
</p>

<p align="center">
  <a href="#install">Install</a> · <a href="#features">Features</a> · <a href="https://holmnotch.ch/docs/">User guide</a> · <a href="CHANGELOG.md">Release notes</a>
</p>

<p align="center">
  <img src="docs/readme/media.png" width="400" alt="Holm's open notch with album art, a seek bar, and playback controls">
  <img src="docs/readme/today.png" width="400" alt="Holm's Today view with the clock, weather, a reminder, and a note">
</p>

## Install

| Platform | Requirements | How to start |
| --- | --- | --- |
| Windows | x64, Windows 10 version 2004 or later, or Windows 11 | Get Holm from the [Microsoft Store](https://apps.microsoft.com/detail/9NZF994ZPFMH), or extract the Windows ZIP and run `Holm.exe`. |
| Linux | x64, X11 or XWayland | Extract the Linux archive and run `./Holm`. |

Both portable builds are available on the [latest GitHub release](https://github.com/ademerci/Holm/releases/latest) and include the .NET runtime. Once Holm is running, move your pointer over the notch at the top of your screen to open it. Right-click the notch to open Settings.

## Features

| Feature | What you can do |
| --- | --- |
| Media | Play, pause, skip, seek, and adjust volume. The notch can pick up colours from the album art. |
| Live updates | See messages, downloads, and battery or connection status at a glance. System integration varies by platform. |
| Everyday tools | Check the weather and your next reminder, jot down a note, or open clipboard history and the calculator. |
| Beam and Shelf | Drop files on the notch to send them to friends or nearby devices. Keep files on the Shelf and drag them out when you need them. |
| Customisation | Rearrange the Start page, show or hide tools, and choose a design and accent colour. |

## Holm in everyday use

- **Skip a track while you work.** The collapsed notch shows what's playing. Hover over it to open the player, skip the track, or turn the volume down, then move your pointer away to return to what you were doing.
- **Find a download as soon as it finishes.** When a browser download completes, the notch pops out with the file name. Use **Show** to jump to it in your file manager.
- **Send a file or set it aside.** Drag a PDF onto the notch and choose a nearby device in Beam. If you need the file in another window, drop it on the **Shelf** and drag it back out once that window is open.

## Screenshots

<p align="center">
  <img src="docs/readme/beam.png" width="400" alt="Beam with a file drop area and options to receive by QR code or a copied code">
  <img src="docs/readme/design.png" width="300" alt="Design settings with theme previews including Prism Glass, Frutiger Aero, and Retro 98">
  <br>
  <em>Beam sharing and the Design picker</em>
</p>

## Help and feedback

- Visit the [user guide](https://holmnotch.ch/docs/) for setup, feature guides, and troubleshooting.
- [Open an issue](https://github.com/ademerci/Holm/issues) to report a bug or suggest a feature. For security reports, follow the [security policy](SECURITY.md).

## Contributing

Holm is built with Avalonia and .NET 10. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then build from the repository root.

**Windows**

```sh
dotnet build Holm.slnx -c Release
```

**Linux x64**

```sh
dotnet build Holm.App/Holm.App.csproj -c Release -r linux-x64
```

See [AGENTS.md](AGENTS.md) for the architecture, tests, and packaging workflow, and [CONTRIBUTORS.md](CONTRIBUTORS.md) for project credits.

## License

Holm is open source under the [MIT License](LICENSE). The AI usage rings and Claude limit reading are ported from [Codenotch](https://github.com/vinzdg/codenotch) by Vinz (MIT License).

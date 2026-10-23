Holm 1.5.5 adds output device switching, friends online status, and a VPN indicator.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.5.5-win-x64.zip** | Everyone. Unzip and run `Holm.exe`, nothing to install. |
| Holm-1.5.5-win-x64-dotnet10.exe | Smaller single exe if you already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

> **“Windows protected your PC”?** The app is not code-signed, so SmartScreen warns on first launch. Click **More info → Run anyway**. Windows Firewall may also ask once – allow it if friends on your network should find you for Beam.

## Changed

- **Switch output devices.** Choose speakers, headphones, headsets, or HDMI audio from the open notch when more than one device is available. Holm shows which device is active and sets the selected device for music, games, and calls.
- **See which friends are online.** When you drag a file onto the notch, friends running Holm show a green dot on their avatar. Presence requests are limited to reduce notification traffic.
- **See VPN connection status.** A lock appears while a VPN is connected, with connection and disconnection notices. The indicator can be turned off from the right-click menu.
- **Safer README snapshots.** Debug snapshot mode clears earlier messages and does not save its temporary layout to your settings.

## Tested

302 automated tests passed on the Release build.

Full list in the [CHANGELOG](https://github.com/ademerci/Holm/blob/v1.5.5/CHANGELOG.md). Open source under the MIT license.

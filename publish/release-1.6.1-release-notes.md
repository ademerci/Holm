Holm 1.6.1 makes available updates easier to spot and gives incoming file requests their own notification sound.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.6.1-win-x64.zip** | Everyone. Unzip and run `Holm.exe`, nothing to install. |
| Holm-1.6.1-win-x64-dotnet10.exe | Smaller single exe if you already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

> **“Windows protected your PC”?** The app is not code-signed, so SmartScreen warns on first launch. Click **More info → Run anyway**. Windows Firewall may also ask once – allow it if friends on your network should find you for Beam.

## Changed

- **A clearer update indicator.** When an update is available, a white download arrow in a blue circle appears immediately after the Updates text in the right-click menu.
- **Incoming file notification sound.** A short chime plays alongside the accept/decline prompt. The sound is bundled with Holm, so it works independently of the Windows system sound scheme.

## Tested

405 automated tests passed on the Release build, including checks for the update menu and the features introduced in 1.6.0.

Full list in the [CHANGELOG](https://github.com/ademerci/Holm/blob/v1.6.1/CHANGELOG.md). Open source under the MIT license.

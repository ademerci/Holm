Holm 1.5.7 makes room for the Bluetooth controls, checks for updates more often, and reduces the size of the single-file downloads.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.5.7-win-x64.zip** | Everyone. Unzip and run `Holm.exe`, nothing to install. |
| Holm-1.5.7-win-x64-dotnet10.exe | Smaller single exe if you already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

> **“Windows protected your PC”?** The app is not code-signed, so SmartScreen warns on first launch. Click **More info → Run anyway**. Windows Firewall may also ask once – allow it if friends on your network should find you for Beam.

## Changed

- **More room in the open notch.** The expanded notch is wider and device badges wrap when needed, keeping Bluetooth, sound, and volume controls visible.
- **More frequent update checks.** Automatic checks run 20 seconds after startup and every two hours after that. Manual checks remain available in the Updates menu.
- **Smaller downloads.** The self-contained single-file build uses compression. Its executable measured 68.6 MB, compared with 158 MB for the same source without compression.
- **Simpler Bluetooth menu.** The redundant *Add a device…* entry is gone. *Bluetooth settings* remains available for pairing new devices.

## Tested

310 automated tests passed on the Release build.

Full list in the [CHANGELOG](https://github.com/ademerci/Holm/blob/v1.5.7/CHANGELOG.md). Open source under the MIT license.

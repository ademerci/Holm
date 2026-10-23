Holm 1.5.8 improves responsiveness while keeping resource use low.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.5.8-win-x64.zip** | Everyone. Unzip and run `Holm.exe`, nothing to install. |
| Holm-1.5.8-win-x64-dotnet10.exe | Smaller single exe if you already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

> **“Windows protected your PC”?** The app is not code-signed, so SmartScreen warns on first launch. Click **More info → Run anyway**. Windows Firewall may also ask once – allow it if friends on your network should find you for Beam.

## Changed

- **Smoother Bluetooth scans.** Battery device enumeration now runs in the background, so it cannot stall the notch. Holm skips a scan if the previous one is still running.
- **Fewer unnecessary redraws.** The open notch updates its clock only when the minute changes and volume visuals only when their displayed values change.
- **Updated lightweight documentation.** The README explains these changes and identifies the CPU and memory figures as measurements from before this release.

## Tested

310 automated tests passed on the Release build. No new CPU or memory benchmark was taken for this release.

Full list in the [CHANGELOG](https://github.com/ademerci/Holm/blob/v1.5.8/CHANGELOG.md). Open source under the MIT license.

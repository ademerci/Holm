Holm 1.7.4 brings the privacy dots inside the notch and restores live call detection when Windows' older microphone records stop updating.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.7.4-win-x64.zip** | Everyone. Unzip and run `Holm.exe`, nothing to install. Also used by Holm's automatic updater. |
| **Holm.exe** | The same self-contained app as a single download. Run it directly; no .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

> **“Windows protected your PC”?** The app is not code-signed, so SmartScreen warns on first launch. Click **More info → Run anyway**. Windows Firewall may also ask once – allow it if friends on your network should find you for Beam.

### Added
- **Live microphone monitoring.** Holm now listens to Windows audio-session events on every recording device, so microphone use and calls can be detected even when the older privacy records have no current entry. Muted recording sessions still count as microphone use. The monitor reads activity metadata, never audio samples.
- **Live camera monitoring.** Holm also listens to Windows' Media Foundation camera-activity reports, without opening a camera or reading frames. Privacy records remain a fallback for other capture paths. Some legacy DirectShow virtual cameras may not be reported by Windows.

### Changed
- **Privacy dots inside the notch.** The orange microphone dot and green camera dot now sit inside the closed notch. With unread messages they appear immediately before the count; idle, call and media views reserve their own space so the dots stay clear of the content.

### Fixed
- **Real Discord calls going undetected.** The old privacy record could remain stale while a new call was active, leaving both the call indicator and microphone dot off. Live recording activity now supplies the missing signal without bringing back calls from old executable versions.
- **Call duration during activity updates.** Repeated reports keep the original start time while recording continues; stopping and starting again gives the new call its own timer.

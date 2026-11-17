# Holm — Architecture & Agent Guide

Holm is a cross-platform desktop notch overlay for **Windows** and **Linux**, built in C# (.NET 10) with **Avalonia UI** and **SkiaSharp**.

---

## 1. Repository Layout

```text
Holm.slnx
├── Holm.App/                  # Avalonia UI (write-once UI for Windows & Linux)
│   ├── Assets/                # HolmIcons.ttf, brand.png, incoming-file.wav, notch.ico
│   ├── Controls/              # Direct-rendered controls: BatteryGlyph, MiniSlider, NotchButton
│   ├── NotchWindow*.cs        # Overlay window, spring physics, views, tools, Beam, Hub, SelfTest
│   ├── ChatPage.cs            # Friends conversations, bubbles, reactions, GIFs
│   ├── ChatExtras.cs          # Reaction bar, full emoji picker (+ customization), GIPHY picker
│   ├── Gifs.cs                # GifInspector, GifDecoder (SKCodec), GifCache, GifPlayer, GifSearch
│   ├── SettingsWindow.cs      # Hub shell + settings switches + ToggleSwitch
│   └── ...                    # ActivityPage, AchievementsPage, AddFriendPage, NetworkPage, ProfileEditor, ColorPickerWindow, StreamWindows
├── Holm.Core/                 # Pure net10.0 domain logic, state, math & networking (0 UI/OS deps)
│   ├── Notify/                # DownloadWatcher, Messengers, NotificationBadges
│   ├── Sharing/               # Beam P2P: Friends, FriendInvite, Chat, FileTransfer, RelayLink, SecureChannel, ShareCode, NearbyService, PhoneShare, ScreenStream
│   ├── Updates/               # UpdateService, UpdateInstaller
│   └── *.cs                   # Settings (AppSettings), Spring, CoverMath, LevelEnvelope, NotchPlacement, Sky, BigClock, Calculator, Notepad, Reminders, Countdown, SleepTimer, ClipboardHistory, ColorPick, Weather, Focumon, SpeedTest, NetworkInfo, AiUsage, Achievements, Today, Shelf, DiskSpace, UsbDrives, VpnNames, Outage
├── Holm.Platform/             # net10.0 platform abstraction interfaces & No* fallbacks
│   ├── IPlatformServices.cs   # Root service container injected into NotchWindow
│   └── I*Platform.cs          # 22 domain interfaces (Window, Media, Audio, Power, Bluetooth, Autostart, Clipboard, Screen, AppVolume, Shell, Connectivity, CapsLock, HotKey, Drives, ClickAway, Foreground, Sensors, Notifications, Identity, Dictation, MediaKeys, Location)
├── Holm.Platform.Windows/     # net10.0-windows10.0.19041.0 backend (Win32, COM, WinRT, DPAPI)
│   ├── Native/                # Windows-only low-level services (VolumeService, AudioMeter, AppVolumes, BluetoothBattery, BluetoothControl, CameraActivity, MicrophoneActivity, SensorRecord, ScreenText, HandyBridge, WeatherLocation, Native)
│   └── Windows*Platform.cs    # IPlatformServices adapter implementations
├── Holm.Platform.Linux/       # net10.0 backend (X11/XWayland, Wayland capture, D-Bus, PulseAudio/PipeWire)
│   ├── Bus.cs                 # Tmds.DBus.Protocol session/system bus client
│   └── Linux*Platform.cs      # IPlatformServices adapter implementations + LinuxScreenGrabber
├── Holm.Tests/                # net10.0 xUnit suite (runs on Windows and Linux)
│   └── Linux/                 # Pure-logic tests for the Linux backend
├── extension/focumon/         # Browser extension feeding Focumon status to FocumonLink
├── tools/
│   ├── icon-font/             # Fluent System Icons builder mapped to Segoe codepoints (HolmIcons.ttf)
│   ├── selftest/compare.py    # Cross-OS screenshot pixel-diff checker
│   ├── MakeIcon.ps1           # Generates Holm.App/Assets/notch.ico & docs/icon.png
│   └── Measure-Footprint.ps1  # Measures idle RAM, peak WS, and CPU on Windows
├── docs/                      # Screenshots & cross-platform dev-release notes
├── publish/                   # Historical release notes (release-<ver>-release-notes.md)
├── website/                   # Static marketing/release site (Node/npm)
└── .github/workflows/         # cross-platform.yml, dev-release.yml, release.yml, footprint.yml, website.yml
```

---

## 2. How It Works

### 2.1 Layering & Dependency Flow
1. **Entry & Platform Wiring (`Holm.App/Program.cs`, `Holm.App/App.axaml.cs`)**
   - `Program.Main` acquires a single-instance lock file (`holm-app.lock` in `Diag.Folder`) and boots Avalonia with `Inter` font.
   - `App.CreatePlatform()` chooses `WindowsPlatformServices` (`HOLM_WINDOWS` compile constant when building for Windows) or `LinuxPlatformServices` (on Linux, falling back to `NoPlatformServices` otherwise).
   - `NotchWindow` receives `IPlatformServices` and never calls OS APIs directly.
2. **Platform Abstraction (`Holm.Platform`)**
   - Capabilities that differ by OS/compositor expose explicit flags (e.g. `IScreenPlatform.CanReadText`, `IMediaKeysPlatform.IsSupported`, `ILocationPlatform.IsSupported`, `IBluetoothPlatform`, etc.) so `Holm.App` hides unsupported switches cleanly.
3. **Overlay Window, Shape & Spring Physics (`Holm.App/NotchWindow.cs`, `NotchWindow.Motion.cs`)**
   - `NotchWindow` stays a fixed transparent overlay (`BaseWidth = 560`) and clips `_backdrop` with a `StreamGeometry` path (concave top shoulders `MaxEar = 10` + rounded bottom corners) rather than resizing the OS window every frame.
   - Five analytical damped harmonic oscillators (`SpringValue`: width, height, corner radius, ear size, scale) step on `RequestAnimationFrame` and stop completely (`_motionRunning = false`) once settled, keeping idle CPU near zero.
   - `UpdateHitRegion` passes `_platform.SetHitRegions` so clicks outside the notch (and optional AI usage bubble) fall through to underlying windows.
4. **Album Art Wash & Direct-Rendered Controls**
   - `MediaService` debounces player updates, deduplicates unchanged timelines (`SameTimeline`/`SameAs`), and runs `CoverMath` on a worker thread: downscales cover art to `24×24`, boosts saturation `1.35×`, applies 3-pass integer box blur, and computes adaptive dark shade opacity.
   - `BatteryGlyph`, `MiniSlider`, and `NotchButton` render directly in `Render(DrawingContext)` without heavy XAML control templates.
5. **Event-Driven Backends**
   - **Windows (`Holm.Platform.Windows`)**: CoreAudio callbacks (`IAudioEndpointVolumeCallback`, `IMMNotificationClient`), GSMTC media sessions, `WH_KEYBOARD_LL` hooks (Caps Lock, `MediaKeys`), `RegisterHotKey` (`Ctrl+H`), `SetupDi`/`IKsControl` Bluetooth batteries, `ConsentStore` registry + CoreAudio/MediaFoundation privacy monitors, `wpndatabase.db-shm` 48-byte header check + `winsqlite3` notifications, WinRT `Geolocator` & OCR (`Windows.Media.Ocr`), GDI `BitBlt` screen grabber, DPAPI key protection.
   - **Linux (`Holm.Platform.Linux`)**: X11 `_NET_WM_WINDOW_TYPE_DOCK` + `XShapeCombineRectangles` hit-masking (works on X11 and XWayland), D-Bus (`Tmds.DBus.Protocol`) for MPRIS2 media, BlueZ Bluetooth & battery, UPower/sysfs battery, NetworkManager connectivity, `libpulse.so.0` (PulseAudio/PipeWire) volume/per-app mixer/loopback meter/mic streams, `/proc/*/fd` + `inotify` camera detection, `dbus-monitor` + Unity LauncherEntry badges, XFixes clipboard/cursor, XKB Caps Lock, `XGrabKey` `Ctrl+H`, Wayland screen capture (`grim` → `org.freedesktop.portal.Screenshot` → `spectacle`/`gnome-screenshot` → error-guarded clamped `XGetImage`), `tesseract` OCR, `xdg-open`/`udisksctl`, and `0600` per-user key sealing (`UnixIdentityPlatform`).
6. **Social, Beam & Chat (`Holm.Core/Sharing`, `Holm.App/ChatPage.cs`, `ChatExtras.cs`, `Gifs.cs`)**
   - End-to-end encrypted (`SecureChannel`, HKDF-SHA256 + AES-GCM) friend invites, chat messages, emoji reactions, GIF links, activity sharing, waves, file/folder ZIP transfers (`RelayLink`, `NearbyService`), and screen streaming (`ScreenStream`).
   - `Gifs.cs` validates every GIF block-by-block (`GifInspector`) before decoding via `SkiaSharp.SKCodec` (`GifDecoder`) with a bounded disk/memory cache (`GifCache`, released on hub close) and visibility/active-window-gated playback (`GifPlayer`).
   - Built-in GIPHY search key can be injected at build time via `-p:GiphyKey=...`, `GIPHY_API_KEY` env var, or a gitignored `giphy.key` file at repo root; otherwise users enter their own key in the GIF picker.
7. **Footprint & Runtime Settings (`Holm.App/Holm.App.csproj`)**
   - Single-file builds intentionally set `<EnableCompressionInSingleFile>false</EnableCompressionInSingleFile>`, `<PublishReadyToRunComposite>false</PublishReadyToRunComposite>`, `<TieredCompilation>false</TieredCompilation>`, `<TieredPGO>false</TieredPGO>`, `<ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>`, and `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>` to minimize idle RAM and background CPU.
   - Fonts (`Inter`, `HolmIcons.ttf`) are bundled with greyscale antialiasing (`Fonts.ApplyRendering`) for consistent cross-OS layout.

---

## 3. Build, Test, Self-Test & Publish

### Build
```powershell
# Full Windows solution (Core, Platform, Platform.Windows, Platform.Linux, App, Tests)
dotnet build Holm.slnx -c Release

# Cross-compile Holm.App targeting Linux x64 (also works from Windows)
dotnet build Holm.App/Holm.App.csproj -c Release -r linux-x64
```

### Tests
```powershell
# Windows
dotnet test Holm.Tests -c Release --filter "Category!=Local&Category!=Online&Category!=Manual"

# Linux (UpdateInstallerTests targets Windows Holm.exe swap)
dotnet test Holm.Tests -c Release --filter "Category!=Local&Category!=Online&Category!=Manual&FullyQualifiedName!~UpdateInstallerTests"
```

### Visual Self-Test
```powershell
$env:HOLM_SELFTEST = "selftest"
dotnet run --project Holm.App -c Release
# Writes closed.png, open.png, report.txt; compare across OSes with:
# python3 tools/selftest/compare.py <folder-with-selftest-Windows-and-selftest-Linux> <out>
```
In debug builds, `$env:HOLM_DEV_TOP = "300"` offsets the notch downward and `$env:HOLM_DATA = "<dir>"` isolates settings/data from the default user directory.

### Publishing
- **Test builds** must be named `Holm-<version>-test-build.<n>.exe` (Windows) and `Holm-<version>-test-build.<n>-linux-x64` (Linux). Reserve `Holm.exe` / `Holm` strictly for official release archives.
```powershell
# Windows x64 self-contained single-file
dotnet publish Holm.App/Holm.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:DebugType=none -o out/win-x64

# Linux x64 self-contained single-file
dotnet publish Holm.App/Holm.App.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:DebugType=none -o out/linux-x64
```

---

## 4. Collaboration Rules

- Keep changes cross-platform first: UI and app state belong in `Holm.App` and `Holm.Core`; OS-specific APIs belong behind `Holm.Platform` interfaces in `Holm.Platform.Windows` and `Holm.Platform.Linux`.
- Preserve existing Git author configuration and add the AI assistant used as co-author when committing.
- Preserve the runtime footprint MSBuild properties in `Holm.App/Holm.App.csproj`.
- Every icon codepoint used in `Holm.App` must exist in `Holm.App/Assets/HolmIcons.ttf` (`tools/icon-font/icons.txt`).

### Releases and branches (carried over from the WPF app's rules)

- Develop general Holm features and fixes on `main`; keep Microsoft Store-specific packaging and release work on `codex/microsoft-store-msix`.
- Periodically merge `main` into `codex/microsoft-store-msix`, including before each Store release. Resolve conflicts and verify that both the portable GitHub version and packaged Store version retain their intended behavior.
- For Store releases, increment the MSIX package version, build and test the package, and complete the Store submission process. The Store manages updates for the packaged app; preserve the existing updater for the portable GitHub release.
- Publish Windows x64 releases as self-contained, single-file builds so users do not need to install .NET, and always name the executable release asset `Holm.exe`, without version or platform suffixes.
- Name test executables `Holm-<version>-test-build.<number>.exe`, with an incrementing build number and an explicit test label.
- Two looks: the classic one is the default; *Settings › General › Instrument look* switches to the instrument look at the next start (`Look.Instrument`). A visual change must keep both.

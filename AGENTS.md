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

### Branches

| Branch | What it holds | Who pushes |
| --- | --- | --- |
| `main` | The released app (Windows and Linux). Every GitHub release is built from it. | Features and fixes, merged when CI is green. |
| `dev` | Work in progress and dev prereleases (`dev-v<x.y.z.n>`). Merged into `main` as a fast-forward when ready. | Anyone. |
| `codex/microsoft-store-msix` | `main` plus the Microsoft Store changes (see *Store release*). Never merged back into `main`. | Only for Store releases. |
| `backup/*` | Snapshots before a risky merge (`backup/main-1.12.11`: the last WPF `main`; `backup/store-1.12.7`). | Never change them. |

- Store-only code (`PackageIdentity`, the startup task, `packaging/`, `store/`, `store.yml`) stays on the Store branch.
- Two looks: the classic one is the default; *Settings › General › Instrument look* switches to the instrument look at the next start (`Look.Instrument`). A visual change must keep both.

### Naming (for AI agents and people)

Several people each work with their own AI agent (Claude, Codex, …) on this repo. Use these names so everyone can tell from the branch list and `git log` what a change is and who made it.

**Branches:** `<type>/<topic>`, lowercase ASCII, words joined with `-`, at most about 5 words.

| Type | For | Example |
| --- | --- | --- |
| `feature/` | Something new in the app | `feature/clipboard-history` |
| `fix/` | A bug fix | `fix/notch-flicker-on-wake` |
| `website/` | Only `website/` | `website/legal-pages` |
| `docs/` | README, AGENTS.md, CHANGELOG, docs | `docs/agent-naming` |
| `ci/` | `.github/workflows`, build and release scripts | `ci/linux-selftest` |
| `release/` | Preparing one release | `release/1.13.1` |

- One branch per change, cut from a fresh `origin/main` (or `dev` for dev work). Merge only when the person who asked for it says so.
- Agents: rename a generated branch (`claude/<random>`, `codex/<random>`) to this pattern before pushing. `codex/microsoft-store-msix` and `backup/*` keep their names.
- No names, umlauts or spaces in branch names (`Maëls-sippi` breaks some tools).

**Commits**

- Subject: one plain English line, at most 72 characters, saying what changes for the user, no full stop. Optional area prefix: `Website:`, `CI:`, `Self-test:`, `Release:`, `Docs:`. A version goes in parentheses at the end: `… (1.13.1)`.
  - Good: `Closed notch shows only the red count for notifications`, `Website: contact form, Impressum and privacy policy`.
  - Not: `feat(ui): update stuff`, `fix bug`, `WIP`.
- Body (when it isn't obvious): why, and what a reviewer should check. Wrap at 72.
- Keep your own Git author. An agent adds itself as the last line: `Co-Authored-By: <Agent and model> <its noreply address>`, e.g. `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Merges: `Merge branch '<branch>': <what it brings>`, or `Merge origin/main (Holm <version>) into <branch>`.

**Pull requests:** title like a commit subject; the description says what changed, how it was tested (tests, self-test, test build) and which agent helped.

**Tags and files:** releases `v<x.y.z>`, dev prereleases `dev-v<x.y.z.n>`, release notes `publish/release-<version>-release-notes.md`, test builds as in *Versions*.

### Versions

- The one version number is `<Version>` in `Holm.App/Holm.App.csproj` (`x.y.z`). The build turns it into the exe's file version `x.y.z.0`.
- Raise it in the first commit after a release that changes the app (`1.13.0` → `1.13.1` for fixes, `1.14.0` for features), never reuse a released number: the release workflow refuses an existing tag.
- Test builds: `Holm-<version>-test-build.<n>.exe` and `Holm-<version>-test-build.<n>-linux-x64`, `<n>` incrementing (CI uses its run number). `Holm.exe` / `Holm` only in official releases.

### GitHub release (portable Windows and Linux)

Every running Holm finds it within two hours: the updater asks `releases/latest` (never a draft or prerelease) and installs `Holm-<version>-win-x64.zip` on Windows; on Linux *Update* opens the release page. A release is therefore final: test before, do not "try" one.

1. On `main`, with the version already raised in `Holm.App.csproj`:
   - `CHANGELOG.md`: the top entry is `## <version> – <YYYY-MM-DD>` (the release day, not "unreleased"), with `### Added` / `### Changed` / `### Fixed` as needed.
   - `publish/release-<version>-release-notes.md`: the release page text (copy the last one: download table, requirements, then the CHANGELOG entry). `publish/` is in `.gitignore`, add the file with `git add -f`.
2. Push and wait for the *Cross-platform* workflow on `main` to pass (tests on both systems, self-test pictures compared, the exe's file version checked).
3. Start *Actions › Release › Run workflow* on `main` with the version (`1.13.0`, no `v`). With the GitHub tools: `actions_run_trigger` (`run_workflow`, workflow `release.yml`, ref `main`, inputs `{"version": "<version>"}`). It checks the version, the notes and that the tag is new, runs the tests, builds with the `GIPHY_API_KEY` secret and publishes `v<version>` as the latest release with exactly these files:
   - `Holm.exe` (the Windows app),
   - `Holm-<version>-win-x64.zip` (only `Holm.exe`, `LICENSE`, `README.txt`: the updater refuses anything else, and an exe whose file version isn't `<version>.0`),
   - `Holm-<version>-linux-x64.tar.gz` (`Holm`, `LICENSE`, `README.txt`).
4. Check the release page: the three files, the notes, marked *Latest*. The Release workflow then starts the *Website* workflow (`gh workflow run website.yml -f deploy=true`; a release made with the workflow token doesn't trigger it by itself). Check that this run passed and its *deploy* job published the site; if not, start *Actions › Website › Run workflow* on `main` with *deploy* on.
5. Never change, delete or re-upload a published release's files; a broken release is fixed by the next version.

### Dev prerelease

For testers only (the updater never offers it): on `dev`, write `<version>.<n>` (e.g. `1.13.1.1`) into `docs/cross-platform/dev-release-version.txt` and push. *Dev release* builds and publishes the prerelease `dev-v<version>.<n>` with test-build names.

### Store release (Microsoft Store)

1. Merge `main` into `codex/microsoft-store-msix` (`git merge origin/main`). Keep the Store changes when resolving conflicts: in Store mode (`PackageIdentity.IsPackaged`) Holm doesn't check or install updates (*Updates are managed by Microsoft Store.*), uses the package's startup task `HolmStartup` instead of the Run key and doesn't take over the old one, and opens Handy's release page instead of downloading its installer.
2. Raise the Store version in `packaging/build-msix.ps1` (`$Version` for `-Store`, four parts ending in `.0`, e.g. `1.13.0.0`) and in `packaging/README.md`, write `packaging/RELEASE-<version>.md` (copy the last one) and point `store.yml`'s artifact at it.
3. Check `store/PRIVACY.md` against what changed since the last Store version (new services, data or permissions).
4. Push; the *Store package* workflow tests and builds `Holm-Store.msix`. Install it on Windows (signed with a local test certificate, see `packaging/README.md`) and check launch, Updates page, Start with Windows after signing in again, and an upgrade over the live Store version.
5. Upload the unsigned `Holm-Store.msix` in Partner Center and submit; the Store signs it. Record the submission in `packaging/RELEASE-<version>.md`.

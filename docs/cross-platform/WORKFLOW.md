# Cross-platform rewrite: how to work on it without disturbing `main`

> **Experimental.** This branch is a work-in-progress rewrite. It is not released, not supported and far from
> feature-complete; the app people use is built from `main` (`WinNotch/`). Do not ship builds from here.

The plan itself is in [PLAN.md](PLAN.md). This file is about the mechanics: where the work lives, what may be
touched, and how it gets back into `main`.

## Where the work lives

- Branch: `experimental/cross-platform`, checked out as its own worktree next to the main checkout
  (`../Holm-crossplatform`). The main checkout stays on `main`; both can be open and built at the same time.
- Everything new goes into new top-level folders: `Holm.Core/`, `Holm.Platform/`, `Holm.Platform.<OS>/`,
  `Holm.App/`, `Holm.Tests/`, plus `Holm.slnx`, `docs/cross-platform/` and
  `.github/workflows/cross-platform.yml`.
- Nobody on `main` edits those paths, and the existing workflows only watch `WinNotch/**` and
  `WinNotch.Tests/**`, so this branch cannot break a release or a test run of the WPF app.

## The one rule that keeps merges clean

**On this branch, do not edit, move or delete anything under `WinNotch/` or `WinNotch.Tests/`.**

Shared code is *linked*, not copied: `Holm.Core.csproj` compiles files straight out of `WinNotch/`, and
`Holm.Tests.csproj` does the same with the tests. One copy of each file exists, the WPF app keeps building it, and a
fix made on `main` reaches the rewrite with a plain merge. Moving the files now would turn every later change on
`main` into a rename conflict.

When a file cannot be linked because it touches WPF or Win32, there are two ways forward:

1. **A seam** (preferred when the change is small): a small pull request against `main` that removes the
   dependency without changing behavior, for example moving constants out of a WPF class or passing a
   callback instead of calling `Native` directly. It ships with the next normal release, then `main` is
   merged into this branch and the file gets linked.
2. **A new implementation** in `Holm.App` or `Holm.Platform.<OS>` for code that is UI or OS plumbing by
   nature. The WPF original stays where it is until the cutover.

## Staying in sync

    git fetch origin
    git merge origin/main        # in the rewrite worktree, at least once a week and after every release

Merge, do not rebase: the branch is long-lived and will be pushed. Because the branch only adds new folders,
these merges should not conflict. After each merge, run:

    dotnet build Holm.slnx
    dotnet test Holm.Tests --filter "Category!=Local&Category!=Online&Category!=Manual"

A build break here means `main` gave a linked file a new Windows dependency; fix it with a seam.

## Getting it back into `main`

Not one big merge at the end. In this order:

1. Seam pull requests go to `main` continuously (see above). They are small and reviewed like any other change.
2. Once `Holm.App` runs on Windows, merge the branch into `main` while it is still purely additive: the new
   folders arrive next to `WinNotch/`, releases still come from `WinNotch/`. Zero conflicts by construction.
3. Ship the Avalonia build as test builds (`Holm-<version>-test-build.<number>.exe`) until it matches the
   WPF build (pixel and footprint checks from PLAN.md section 7).
4. Cutover in a single commit on `main`: switch `release.yml` to `Holm.App`, `git mv` the linked files into
   `Holm.Core/`, delete `WinNotch/`. Merge `main` into `codex/microsoft-store-msix` right after.


## Status

Last merge from `main`: Holm 1.12.5. Its notch changes are ported too (a tool's field takes the keyboard when the
tool opens, the reminder hint when no time is found, the double-click guard on removing a reminder).

**Linked into `Holm.Core`:** 37 source files, with 27 test files (443 tests) in `Holm.Tests`.

**`Holm.App`** (checked on Windows by running it):

- `NotchWindow.cs`: fixed transparent overlay, the shape with its shoulders as a clip, the WPF app's springs,
  opening on hover, click-through beside the notch.
- `NotchWindow.Views.cs`: idle clock, compact player (cover, equalizer bars at rest), open notch with the player
  (cover, title, progress, seek, shuffle/previous/play/next/repeat) or the clock, and the cover's colour wash.
- `MediaService.cs`: shared debounce, de-duplication and cover handling on top of `IMediaPlatform`.
- `Controls/MiniSlider.cs`, `Controls/NotchButton.cs`: drawn directly, no templates.
- Volume: the pop-out when the level changes (not yet seen running), mute button and slider in the open notch.
- Equalizer: follows the sound through the audio backend, or bounces when it can't be read (not yet seen running).
- `NotchWindow.Tools.cs`: the tool row with notes and the calculator, and the keyboard hand-over (the notch takes
  the keyboard only while you click into one of its text fields).
- `NotchWindow.Reminders.cs`: the reminders page, the next reminder in the idle notch, the card when one is due.
- `NotchWindow.Popups.cs`: the peek (one line for a few seconds; nothing calls it yet) and the task card with
  buttons.
- `NotchWindow.Timer.cs`: the timer (closed view, row in the open notch, in place of the equalizer while music
  plays, the card when it is done) and the right-click menu, so far with Timer and Quit.
- `NotchWindow.Devices.cs`: battery and Bluetooth chips in the open notch, and the peeks for charging, low battery
  and a device connecting (the peeks are not yet seen running). Bluetooth is read at start and each time the
  notch has opened; nothing watches it in between yet.
- Notes and reminders live only as long as the port runs: settings are read-only. No alarm sound yet.
- `Holm.Platform.Windows`: overlay window styles and click region, media sessions (GSMTC), volume and
  levels (CoreAudio), battery, Bluetooth batteries. `VolumeService.cs`, `AudioMeter.cs`, `Native.cs` and
  `BluetoothBattery.cs` are linked from `WinNotch/` into this project:
  Windows-only files that never touch WPF need no rewrite, only an interface in front.

The port reads the installed app's `settings.json` (animations, bounce, reactive equalizer) and never writes it
(`AppSettings.ReadOnly`). With `HOLM_DATA=folder` (Debug builds) it runs on a data folder of its own and saves there.

Run it next to an installed Holm with `HOLM_DEV_TOP=300` (Debug builds only): the notch sits 300 px down.

Known gaps in what is ported: clipboard, mixer and colour picker are missing from the tool row; views fade but do not pop
or unfold yet; a new cover fades in over black instead of cross-fading; no scale settings, mini idle or multi-monitor choice; the context menu has only Timer and Quit. Icons use Segoe Fluent Icons by name, which
exists on Windows only and may not be redistributed: Linux and macOS need a replacement icon font.

**Seams made so far.** These are the only edits under `WinNotch/` on this branch. They change no behavior (the
WPF suite passes) and belong on `main`: commit them separately from the new folders, so that commit can be
cherry-picked into a pull request against `main`.

| Seam | Files |
|---|---|
| Clock keys and formatting apart from WPF swatches and fonts | `BigClock.cs`, new `BigClock.Wpf.cs` |
| Autostart marked Windows-only, its call guarded | `Settings.cs` |
| `Spring` and `SpringValue` out of the window code | new `Spring.cs`, `MainWindow.Motion.cs` |
| Cover blur, shade and accent maths on plain bytes | new `CoverMath.cs`, `MediaService.cs` |
| `LevelEnvelope` apart from the WASAPI meter | new `LevelEnvelope.cs`, `AudioMeter.cs` |

**Still blocked,** each a candidate for the next seam:

| Blocked files | Reason |
|---|---|
| `Achievements.cs`, `Weather.cs` tests (`Sky`), `BigClockTests.cs`, `MotionTests.cs` | Use `System.Windows.Media.Color`; need a small colour type of our own in Core. |
| `FriendsService.cs` and the tests around it | WPF `Dispatcher` (use `IUiDispatcher`) and DPAPI (`ISecretStore`). |
| `ExitWatchdog.cs`, `ColorPick.cs` | Call `Native` directly. |
| `Calls.cs` | Needs `SensorUse` from `PrivacyMonitor.cs`. |
| `NetworkInfo.cs` | Needs `NetworkLink` from `SpeedTest.cs` (WinRT). |
| `Notify/Messengers.cs` tests | Need `BadgeCounter`. |
| `UpdateServiceTests.cs` | Expects a built `Holm.exe` next to the test assembly. |

**Next in `Holm.App`:** the remaining closed views (badge, call), the Bluetooth and output-device buttons, then
the rest of the context menu with the settings switches (which needs writable settings).

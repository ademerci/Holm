Holm 1.11.0 opens more smoothly, gives the open notch a sky when nothing plays, and uses less CPU while idle.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.11.0-win-x64.zip** | Unzip and run `Holm.exe`; also used by Holm's updater. |
| **Holm.exe** | Run the self-contained app directly; no .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

Holm is not code-signed. Windows SmartScreen may warn on first launch; Smart App Control may block an unsigned app.

### Added
- **A sky when nothing plays.** Open with no music, the notch glows in the colours of the time of day (dawn, day, dusk, night with a few stars) and of the weather (rain, snow, fog, clouds), greets you with *Good morning*, *Good afternoon* or *Good evening* and your first name, and the clock rolls over to each new minute. It is drawn once and stays still, so it costs no CPU while you look at it.

### Changed
- **Smoother opening and closing.** The notch springs open with a slight bounce and glides shut. The rows of the open notch unfold from under the closed one, and the small views pop in. Opening no longer waits for the list of audio outputs or a first layout of everything in it.

### Fixed
- **Less CPU while idle.** The notch made sure it stayed on top every 1.5 seconds, which made it redraw about 40 times a minute with nothing changing. It now only does that when another window really covers it: idle CPU went from 0.7–1.3 s to 0.3–0.5 s per minute on our test laptop.
- **Memory after the Holm window.** Closing the Holm window lets go of its decoded chat GIFs and hands the memory back to Windows a few seconds later.

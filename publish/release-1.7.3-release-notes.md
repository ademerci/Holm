Holm 1.7.3 keeps the open notch familiar with or without music, makes settings easier to apply, and fixes calls that stayed on after they ended.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.7.3-win-x64.zip** | Everyone. Unzip and run `Holm.exe`, nothing to install. Also used by Holm's automatic updater. |
| **Holm.exe** | The same self-contained app as a single download. Run it directly; no .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

> **“Windows protected your PC”?** The app is not code-signed, so SmartScreen warns on first launch. Click **More info → Run anyway**. Windows Firewall may also ask once – allow it if friends on your network should find you for Beam.

### Added
- **Updates in the notch.** A new version gets a clickable notification to update and restart Holm. It stays in the notification list and on the closed notch's badges until dismissed, and the same version does not keep popping out.
- **Dismiss notifications.** Close individual message or update rows, or use *Clear all* above the message list. Dismissing an update notification still leaves the update available in the menu.
- **Media controls switch.** *Settings › Open notch › Media controls* is on by default. Turn it off to keep the clock and today card even while Spotify, YouTube or another player is playing, without track details, artwork or album backgrounds. Music pausing for calls and the sleep timer are also inactive while it is off; saved choices are preserved.
- **Weather without typing a city.** If no location is saved, Holm tries Windows approximate location once at startup. Coordinates are rounded to one decimal place before being saved or sent to Open-Meteo, then reused; there is no separate IP lookup service or continuous location tracking. Windows location services must be on. Otherwise, choose a city manually. Automatic setup has its own Settings switch, and *Customize › Weather › Use approximate Windows location* detects again when you want it to.

### Changed
- **One toolbar in the same place.** The top tool buttons stay put with or without music and while a tool page is open. The duplicate calculator, color picker, clipboard and app-volume buttons are gone from the idle card. Idle time and date stay at the top below the toolbar; with media controls on, a smaller time and date remain at the upper left. The sleep timer stays aligned at the right.
- **Apply settings when ready.** Switches stage changes until you click *Apply*. *Reset to defaults* prepares the default choices, and closing with unsaved changes offers to apply or discard them. Confirmation dialogs stay inside Settings and blur the whole surface behind them; action buttons have clearer hover, press and keyboard-focus states.

### Fixed
- **Discord calls that never ended.** An old Discord version could leave a microphone record marked active, and Holm mistook any running Discord process for that old call. Desktop sensor records now have to match the executable path and a process that was already running when recording began. Active records are rechecked so exited apps clear even if Windows never writes a stop time; a new call in the same app also gets its own start time.
- **Menus reopening on a second click.** Clicking the Bluetooth, audio-output or sleep-timer button again now closes its menu instead of immediately opening it again.
- **Animations really turn off.** Disabling animations also stops notch shape transitions and view fades, including transitions that were already running.
- **Weather setup during playback.** Choosing a weather city opens the setup even while media is playing, then returns to the player. Loading and failed forecasts are visible, with a way to choose another city instead of leaving an empty space.

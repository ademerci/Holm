Holm 1.17.0, Lundy, gives the Dot Matrix design real dots: clocks and titles in a dot font on a fine dot grid. It also brings the optional Holm account: the same settings, profile, achievements and friends on every computer, end-to-end encrypted.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.17.0-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-1.17.0-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Added
- **Dot Matrix in real dots.** The Dot Matrix design now writes the clock in the notch, the big clock, the weather temperature and the page titles of the Holm window in a dot font, and lays a fine grid of dots over the notch and the sidebar, like the dot display on the back of a phone. Pick it under *Settings › Design*. The other designs look as before. Made by Diekartoffel19.
- **A new series, Lundy.** The Holm window shows the name beside the version number.
- **Holm account, if you want one.** Sign in on each computer and your settings, profile, achievements and friends stay the same everywhere, with one friend code. It sits on the profile page under your profile, and Holm offers it once at the first start. Everything is encrypted on your computer before it goes: the account service (Google Firebase, stored in Zürich) keeps your email address and data it can't read. Notes, reminders, chats and files stay on each computer. A forgotten password can't be reset. Without an account Holm works as before.

### Fixed
- **Files in chats and Beam arrive after Accept.** After the other person pressed *Accept*, the transfer could stand still at 0 % on both sides: the receiver waited for its own check "did the sender cancel?" to stop before answering, and on some systems that check never stopped, while the sender waited for the answer. The receiver now answers straight away.

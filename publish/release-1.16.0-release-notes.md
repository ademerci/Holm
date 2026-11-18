Holm 1.16.0, Skokholm, brings the Holm account: the same settings, profile, achievements and friends on every computer you sign in on, end-to-end encrypted.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.16.0-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-1.16.0-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Added
- **Holm account, if you want one.** Sign in on each computer and your settings, profile, achievements and friends stay the same everywhere, with one friend code. It sits on the profile page under your profile, and Holm offers it once at the first start. Everything is encrypted on your computer before it goes: the account service (Google Firebase, stored in Zürich) keeps your email address and data it can't read. Notes, reminders, chats and files stay on each computer. A forgotten password can't be reset. Without an account Holm works as before.

### Fixed
- **Files in chats and Beam arrive after Accept.** After the other person pressed *Accept*, the transfer could stand still at 0 % on both sides: the receiver waited for its own check "did the sender cancel?" to stop before answering, and on some systems that check never stopped, while the sender waited for the answer. The receiver now answers straight away.

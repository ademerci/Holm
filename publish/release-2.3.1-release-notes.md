Holm 2.3.1 — Jethou fixes the notch, Linux network details and reminders on Windows and Linux. It also includes a notes fix in the mobile source. The downloads below are for Windows and Linux.

## Download

| File | For whom |
| --- | --- |
| **Holm-2.3.1-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-2.3.1-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Fixed

- **Verity in the Instrument layout.** When the open notch has no music playing, Verity moves into the free space as the layout changes and keeps clear of the toolbar.
- **Notch animation on Linux.** The notch fits each animated frame inside the window while X11 catches up with a resize.
- **Network details on Linux.** The Network page keeps showing adapter details, including addresses, gateways and DNS servers.
- **Reminders for today.** A time that has already passed is rejected when you explicitly say “today.” A time without a date still rolls over to tomorrow.
- **Reminder titles.** Day words and durations in the title stay in the title. For example, “in 20 min call tomorrow’s guests” and “tomorrow 08:15 walk 20 min” use the intended time.
- **Reminders across clock changes.** A two-hour reminder takes two elapsed hours across daylight-saving changes. On the fall-back night, a repeated clock time uses the first occurrence that is still ahead.
- **Long reminder lists.** Scroll to see and remove reminders beyond the first six.
- **Re-enabling reminders.** Pending reminders start firing again when Reminders is turned back on.
- **Mobile notes.** The editor and saved note use the same 4,000-character limit. Pasted text is clipped at the limit without splitting emoji.

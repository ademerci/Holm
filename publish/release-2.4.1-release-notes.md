Holm 2.4.1 — Brecqhou locks friend inboxes to their owner's read token and stops Verity twitching.

## Download

| File | For whom |
| --- | --- |
| **Holm-2.4.1-win-x64.zip** | Unzip and run `Holm.exe`. Holm's updater uses this file too. |
| **Holm.exe** | Run the self-contained app directly. No .NET installation needed. |
| **Holm-2.4.1-linux-x64.tar.gz** | Unpack and run `./Holm`. No .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64, or Linux x64 with X11 (Wayland through XWayland).

Holm is unsigned. Windows SmartScreen may warn on first launch, and Smart App Control may block an unsigned app.

### Fixed

- **Verity stays calm.** She no longer twitches on short stretches of the notch or after being thrown.

### Security

- **Friend inboxes need their read token.** The relay no longer hands out an inbox's sealed notes without the owner's read token, so Holm older than 2.4 can't read friend notes any more; update Holm. New inboxes are named after their read token, so nobody can claim one before its owner.

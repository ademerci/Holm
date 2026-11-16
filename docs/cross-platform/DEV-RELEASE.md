**⚠️ Dev build for testing only – not a release.**

This is a test build of the cross-platform rewrite of Holm (branch `dev`), for **Windows and Linux**. It is meant
to be tried and reported on, not used day to day. Things may be missing, look different or break. Please test it
and tell us what doesn't work or doesn't look the same as the normal Holm.

The normal Holm (built from `main`) does not update to this build: its updater only takes full releases.

### Downloads

- **Windows x64:** `Holm-<version>-test-build.<n>.exe` – one file, no .NET needed. On Windows it reads the normal
  Holm's settings but does not change them.
- **Linux x64:** `Holm-<version>-test-build.<n>-linux-x64` – one file. Make it executable (`chmod +x`) and start
  it. Needs an X11 session (on Wayland through XWayland). Messages from other apps need `dbus-monitor`.

### What to test

- The notch: open and close, sizes, the display it sits on, the big clock, today cards, hiding over fullscreen apps
- Music and volume, the mixer, Bluetooth and battery, downloads, screenshots, USB drives, Caps Lock, VPN
- Microphone/camera dots and calls (music pauses for a call)
- Messages from WhatsApp, Discord, Telegram, Signal, Slack, Outlook: pop-outs, badge, list, quiet hours
- Beam: drop files on the notch and send them to a friend, to someone nearby or with a code
- Friends and chat (right-click › Friends and chat…)

### Not in this build yet

Beam text, send later, phone, screen streaming, waves, activity, achievements, profile editor, GIFs in chat,
screenshot text, dictation, Focumon, the AI button, updates, Wi-Fi and speed test.

# Focumon for Holm

Companion Chrome/Edge extension: reads your Focumon focus/break timer on the page and shows it in Holm’s open notch.

## Load it

1. Open `chrome://extensions` (or `edge://extensions`).
2. Turn on **Developer mode**.
3. **Load unpacked** → choose this `extension/focumon` folder.
4. Keep Holm running, open [focumon.com](https://www.focumon.com/app), start a focus session, and open the notch.

Nothing leaves this PC: the extension only POSTs to `127.0.0.1:47631`.

Focumon’s own Chrome Web Store extension (popup window) is separate. You need **this** unpacked extension for the Holm chip.

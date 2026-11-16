# Holm 1.12.7 Store release

- App version: 1.12.7; MSIX version: 1.12.7.0; architecture: x64.
- Merged origin/main at 63290ab (GitHub release v1.12.7) into codex/microsoft-store-msix.
- Preserved Store-managed updates, packaged startup and the Store-specific Handy download behavior.
- The Store package workflow runs automated tests and builds with the existing GIPHY_API_KEY Actions secret.

## Customer release notes

Holm 1.12.7 (changes since 1.12.0)

- Move the notch along the top of your screen. Turn on Drag notch position in Settings > General, drag its top edge, and Holm remembers the position. Reset Position centres it again.
- AI usage ring for Codex and Claude Code, with usage and active sessions read locally. Codex limits now show their reported duration and reset date, and outdated readings are labelled.
- Ctrl+H opens and closes the notch, with a switch in Settings > General.
- Large clock view: click the clock in the open notch, pin it open, and customize its date, seconds, font and colour.
- Sky colours can be switched off in Settings > Weather.
- Faster startup, a smaller app, clearer Bluetooth battery rows and larger notification icons.
- Better keyboard and screen-reader support, clearer settings and Beam messages, and automatic focus in tools.
- Fixes for double clicks, weather searches and speed tests that could get stuck.

## Validation

Pending the Store package workflow and Partner Center validation. Hardware-dependent tests and Windows App Certification Kit require separate validation.

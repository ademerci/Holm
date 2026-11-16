# Holm 1.12.7 Store release

- App version: 1.12.7; MSIX version: 1.12.7.0; architecture: x64.
- Merged origin/main at 63290ab (GitHub release v1.12.7) into codex/microsoft-store-msix.
- Preserved Store-managed updates, packaged startup and the Store-specific Handy download behavior.
- The Store package workflow runs automated tests and builds with the existing GIPHY_API_KEY Actions secret.

## Customer release notes

Holm 1.12.7 (changes since the live Store version 1.11.1)

- Move the notch along the top of your screen. Turn on Drag notch position in Settings > General, drag its top edge, and Holm remembers the position. Reset Position centres it again.
- AI usage ring for Codex and Claude Code, with usage and active sessions read locally. Codex limits now show their reported duration and reset date, and outdated readings are labelled.
- Ctrl+H opens and closes the notch, with a switch in Settings > General.
- Large clock view: click the clock in the open notch, pin it open, and customize its date, seconds, font and colour.
- Sky colours can be switched off in Settings > Weather.
- Faster startup, a smaller app, clearer Bluetooth battery rows and larger notification icons.
- Better keyboard and screen-reader support, clearer settings and Beam messages, and automatic focus in tools.
- Fixes for double clicks, weather searches and speed tests that could get stuck.

- Bouncier, physics-based motion with a setting to tone it down, plus a Chats button in the open notch.
- Dictation with Handy: Holm shows when Handy listens. Handy is installed separately.
- All notifications in the notch, and an option to handle volume and media keys without duplicate Windows pop-ups.
- Wi-Fi details and a speed test; Tailscale support in the VPN indicator.
- Faster chats, lower GIF memory and CPU use, more reliable file transfers, and screen sharing that handles slow connections better.
- Thanks to Djordje Mojsilovic for the 1.12 performance and interaction improvements.
## Validation

- GitHub Actions run 43959697301: all 1149 automated tests passed, zero failures or skips (Local, Online and Manual categories excluded).
- Self-contained x64 publish and MakeAppx package validation passed; unsigned MSIX size: 64,816,666 bytes.
- Verified Store identity HolmTeam.HolmforWindows, package version 1.12.7.0, Windows.Desktop minimum 10.0.19041.0 and existing capability/startup declarations.
- MSIX SHA256: 4EAE0E8F1321CFC3954FBFCDC43A04D06268313DCA32BD8873C18311F6D94A15.
- Windows App Certification Kit, clean-machine upgrade, sign-in startup and hardware integration retests were not run; these limits are disclosed in the reviewer instructions.

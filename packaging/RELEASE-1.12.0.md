# Holm 1.12.0 Store release

- App version: 1.12.0; MSIX version: 1.12.0.0; architecture: x64.
- Merged origin/main (Holm 1.12.0, released on GitHub as v1.12.0) into codex/microsoft-store-msix.
- Store-specific: without Handy installed, *Get Handy* opens Handy's GitHub release page in the browser instead of downloading and running its installer.
- `build-msix.ps1` also finds MakeAppx in the Microsoft.Windows.SDK.BuildTools NuGet package when the Windows SDK isn't installed.
- Preserved packaged update guards and the portable GitHub updater.

## Validation

- 1111 automated tests passed, no failures or skips.
- `build-msix.ps1 -Store` built `Holm-Store.msix` (64.8 MB, unsigned for Partner Center) with identity HolmTeam.HolmforWindows, version 1.12.0.0.
- Windows App Certification Kit not run (needs administrator elevation).

## Customer release notes

Holm 1.12.0

- The chat opens in a moment and keeps up, even with long conversations.
- GIFs use far less memory and CPU.
- Sending files to friends no longer gets stuck, and screen sharing holds up on slow connections.
- New Chats button in the open notch's tool bar.
- Dictation with Handy: the notch shows when Handy listens (Handy is installed separately).
- All notifications in the notch: every app's notification can pop out at the top.
- Volume and media only in the notch: Holm handles the volume and media keys so Windows' own pop-up doesn't show twice.
- Bouncier, physics-based motion, with a setting to tone it down.
- Thanks to Djordje Mojsilovic for this release.

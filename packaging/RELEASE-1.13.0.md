# Holm 1.13.0 Store release

- App version: 1.13.0; MSIX version: 1.13.0.0; architecture: x64.
- Merged origin/main at 2b69dd6 (Holm 1.13.0, the cross-platform Avalonia app that replaces the WPF app) into
  codex/microsoft-store-msix. The previous Store branch is kept as `backup/store-1.12.7`.
- Ported the Store behaviour to the new app: `PackageIdentity` (Holm.Core) turns off Holm's own update checks and
  installer (*Updates are managed by Microsoft Store.*), uses the package's startup task `HolmStartup` in place of the
  Run key and skips taking over the Run key, and opens Handy's release page instead of downloading its installer.
- `packaging/build-msix.ps1` publishes `Holm.App` as the GitHub release does (self-contained, single file, ready to
  run) and packs it as `Holm.exe`. The Store package workflow runs `Holm.Tests`.
- Privacy policy: AI usage rings now ask Anthropic for Claude's limits with Claude Code's sign-in (since 1.12.8).

## Customer release notes

Holm 1.13.0 (changes since the Store version 1.12.7)

- A new foundation: Holm is rebuilt with a faster, modern UI framework. Your settings, notes, reminders, friends and
  chats carry over.
- The classic look stays the default; Settings > General > Instrument look switches to a black notch with white ink,
  small corners and tools as words.
- AI usage rings at both ends of the closed notch for Claude and Codex, with an AI usage page showing every limit and
  its reset time. Claude's percentages are the real limits from Anthropic when Claude Code is signed in.
- Settings descriptions rewritten in plain words.

## Validation

- To do before submitting: Store package workflow (tests, publish, MakeAppx), install the package on Windows and
  check launch, the Updates page, Settings > Start with Windows (sign out and in), notifications, media, Bluetooth,
  Beam, dictation (Handy's page opens), and an upgrade from the live 1.12.7 package with its settings.

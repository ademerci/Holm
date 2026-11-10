# Repository collaboration preferences

- When committing changes, preserve the configured Git author and add the AI assistant used as a co-author in the commit message.

## Test builds

- Name test executables `Holm-<version>-test-build.<number>.exe`, with an incrementing build number and an explicit test label. Reserve `Holm.exe` for release assets.

## Release packaging

- Publish Windows x64 releases as self-contained, single-file builds so users do not need to install .NET.
- Always name the executable release asset `Holm.exe`, without version or platform suffixes.

## GitHub and Microsoft Store releases

- Develop general Holm features and fixes on `main`; keep Microsoft Store-specific packaging and release work on `codex/microsoft-store-msix`.
- Periodically merge `main` into `codex/microsoft-store-msix`, including before each Store release. Resolve conflicts and verify that both the portable GitHub version and packaged Store version retain their intended behavior.
- For Store releases, increment the MSIX package version, build and test the package, and complete the Store submission process. The Store manages updates for the packaged app; preserve the existing updater for the portable GitHub release.
- Keep GitHub Windows x64 executable releases self-contained and single-file, named `Holm.exe`.

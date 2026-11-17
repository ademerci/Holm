---
name: holm-ship-feature
description: >
  How a feature or fix gets shipped in Holm, from branch to GitHub release and Microsoft Store.
  Use whenever someone wants to build, fix, ship, release or publish something in this repo
  ("ship this", "new feature", "make a release", "push to the Store", "open a PR into main").
  Works together with the ponytail skill: ponytail decides how much code, this skill decides the steps.
---

# Shipping in Holm

Holm is a WPF (.NET 10, Windows x64) notch app in `WinNotch/`, with xUnit tests in `WinNotch.Tests/`,
a static website in `website/` (GitHub Pages, holmnotch.ch) and a Microsoft Store build on its own branch.
Follow these steps in order. Explain each step to the person in plain words (what and why): they are an
apprentice who wants to learn, not just get a diff.

## 1. Branch

- General features and fixes start from `main` on a branch named `feature/<short-name>` (fixes: `fix/<name>`).
  In a cloud session use the branch the session was given instead.
- Store-only packaging work goes on `codex/microsoft-store-msix`, never on `main` (see AGENTS.md).

## 2. Build it the Holm way (with ponytail, level full)

- Load the `ponytail` skill and take the smallest complete change.
- Look at how a neighbouring feature is built and copy its shape:
  - logic without WPF in `WinNotch/<Feature>.cs` (testable, e.g. `AiUsage.cs`, `ClaudeLimits.cs`);
  - the UI as a partial class `WinNotch/MainWindow.<Feature>.cs`;
  - an on/off switch: property in `Settings.cs`, menu item + one-line description in `MainWindow.Settings.cs`;
  - tests in `WinNotch.Tests/<Feature>Tests.cs`. Tests that need hardware, the internet or a human get
    `[Trait("Category", "Local" | "Online" | "Manual")]`; CI skips those.
- Holm's house rules that show in the code: read-only and private by default (nothing leaves the PC unless the
  feature needs it, and the README/CHANGELOG says so), cheap when idle (timers back off, no work while a
  fullscreen app runs), respect `_settings.Animations`, give icon-only buttons an AutomationProperties name.
- Comments explain the why in plain English, in the style of the surrounding file.

## 3. Check

- Tests (Windows only, the same command CI runs):
  `dotnet test WinNotch.Tests -c Release --filter "Category!=Local&Category!=Online&Category!=Manual" -- xUnit.ParallelizeTestCollections=false`
- In a Linux cloud session WPF does not build: say so, push the branch and let the **Test** workflow
  (`.github/workflows/test.yml`, runs on every push/PR touching `WinNotch/**`) be the check. Report its result.
- A test exe you hand to someone is named `Holm-<version>-test-build.<n>.exe` (n counts up). `Holm.exe` is only for releases.
- Website changes: `cd website && npm ci && npm run check && npm run lint && npm run build && npm test`.
- Then run `ponytail-review` on the diff.

## 4. Document

- `README.md`: add or update the feature's bullet if users can see it.
- `CHANGELOG.md`: add the entry under the next version only when a release is being prepared (step 6);
  until then describe the change in the commit/PR. Style: **Bold title.** then what the user notices, then privacy/cost notes.
- Credit a human contributor in `CONTRIBUTORS.md` if they are not listed yet.

## 5. Commit and merge into main

- Keep the configured Git author; end the message with the AI co-author line (AGENTS.md).
- Message style: short sentence of what the user gets, e.g. "Show actual Codex usage windows and dated resets".
- Into `main` either by pull request (external contributors and bigger changes) or a merge commit
  `Merge branch 'feature/x': <summary>`. Ask before pushing to `main` or opening a PR.
- Merging into `main` does NOT release anything to users. It only runs the tests, and redeploys the
  website if `website/**` or `docs/**` changed.

## 6. Release on GitHub (maintainer, by hand)

1. One commit "Prepare Holm X.Y.Z release": bump `<Version>` in `WinNotch/WinNotch.csproj`, add
   `## X.Y.Z – YYYY-MM-DD` to `CHANGELOG.md`, add `publish/release-X.Y.Z-release-notes.md`
   (copy the previous file's layout: one-line summary, Download table, then the changelog section).
2. Push to `main`.
3. GitHub › Actions › **Release** › Run workflow with `X.Y.Z`. It checks version/notes/tag, runs the tests,
   builds the self-contained single-file `Holm.exe` + `Holm-X.Y.Z-win-x64.zip` and publishes tag `vX.Y.Z`.
4. Automatic after that: the **Website** workflow rebuilds and deploys on `release: published`, and portable
   users' built-in updater finds the new zip.

## 7. Microsoft Store (maintainer, by hand, on Windows)

1. Merge `main` into `codex/microsoft-store-msix`; resolve conflicts so the portable build keeps its updater
   and the packaged build lets the Store update it (`PackageIdentity.cs`).
2. Raise the default Store version in `packaging/build-msix.ps1` (four parts, last is `.0`) and in `packaging/README.md`;
   add `packaging/RELEASE-X.Y.Z.md`.
3. `powershell -ExecutionPolicy Bypass -File .\packaging\build-msix.ps1 -Store` → `artifacts\msix\Holm-Store.msix` (unsigned; the Store signs it).
4. Run Windows App Certification Kit, test startup, notifications, Bluetooth, weather, Beam, upgrade.
5. Upload and submit in Partner Center; certification takes hours to days.
6. Record it on the branch, and update `website/data/store.json` on `main` so the website shows the Store version.

## Report

End with: what shipped where (branch / main / release / Store), what was verified and how, what was not
(e.g. "WPF tests not run here, waiting on CI"), and the next manual step for the maintainer.

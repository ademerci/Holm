# Experimental branch: cross-platform rewrite

This branch (`experimental/cross-platform`) is a work-in-progress rewrite of Holm on Avalonia, meant to run on
Windows, Linux and macOS one day.

- **Not released, not supported, far from feature-complete.** Do not ship builds from here.
- The Holm people use is built from `main`, out of `WinNotch/`. That code is unchanged here except for a few
  behavior-neutral refactors.
- The rewrite lives in `Holm.Core/`, `Holm.Platform/`, `Holm.Platform.Windows/`, `Holm.App/` and `Holm.Tests/`.
- So far only a Windows backend exists, and only part of the notch is ported.

What is done, what is missing and how the branch stays mergeable: [docs/cross-platform/WORKFLOW.md](docs/cross-platform/WORKFLOW.md).
The plan: [docs/cross-platform/PLAN.md](docs/cross-platform/PLAN.md).

Try it next to an installed Holm (Debug build, the notch sits 300 px below the top):

    $env:HOLM_DEV_TOP = "300"
    dotnet run --project Holm.App

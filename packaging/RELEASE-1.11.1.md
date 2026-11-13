# Holm 1.11.1 Store release

- App version: 1.11.1; MSIX version: 1.11.1.0; architecture: x64.
- Merged origin/main, including 1.11.0 and 1.11.1, into codex/microsoft-store-msix.
- Preserved packaged update guards and the portable GitHub updater.
- Updated both privacy policy contact links to contact@holmnotch.ch and the effective date to November 13, 2026.
- Updated Partner Center support contact, customer release notes, description and weather feature text. Retained the existing launch trailer, pricing, availability and automatic publishing preference.

## Validation

- 982 automated tests passed, no failures or skips.
- Self-contained win-x64 single-file publish and MakeAppx validation succeeded.
- Manifest retains the existing Store identity, publisher, desktop minimum version and capabilities; package version is higher than live 1.10.0.0.
- Debug snapshot exercise completed with exit code 0. Generated images are not used for the listing because expansion poses did not remain open; existing listing screenshots and trailer are retained.
- Windows App Certification Kit was attempted, but Windows requires administrator elevation, so no current WACK report was produced. Prior certification caveats in packaging/CERTIFICATION.md still apply. Clean-machine upgrade, sign-in startup and hardware-dependent integration checks were not repeated in this session.
- Partner Center accepted and saved the uploaded 1.11.1.0 package, with its existing runFullTrust approval notice.

## Customer release notes

Holm 1.11.1

- A new idle view brings time-of-day and weather colors, subtle night stars, a personal greeting and a rolling clock to the open notch when no music is playing.
- Smoother opening and closing, with a gentle spring and quicker access to the open notch.
- Lower idle CPU use and improved memory recovery after closing the Holm window.
- Minimize over browser tabs now keeps a small pill within reach. Hover or click to open it; notifications and other activity still get their usual space.
- Sunny daytime weather now has a muted golden glow.
- Updated support and privacy contacts to contact@holmnotch.ch.
- Store app updates continue through Microsoft Store.
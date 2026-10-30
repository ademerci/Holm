Holm 1.7.5 adds regression coverage for the privacy indicator. There are no user-facing changes from 1.7.4.

The new test checks that an active camera indication clears when Windows reports that camera use has stopped, even if Holm misses the stop notification. The privacy-monitoring behavior itself is unchanged.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.7.5-win-x64.zip** | Unzip and run `Holm.exe`; also used by Holm's automatic updater. |
| **Holm.exe** | Run the self-contained app directly; no .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

Holm is not code-signed, so Windows SmartScreen may warn on first launch. Click **More info → Run anyway** to launch it.

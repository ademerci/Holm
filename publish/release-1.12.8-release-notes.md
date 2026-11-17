Holm 1.12.8 brings Codenotch's AI usage rings into the notch, with Claude's real limits and a page for every limit.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.12.8-win-x64.zip** | Unzip and run `Holm.exe`; also used by Holm's updater. |
| **Holm.exe** | Run the self-contained app directly; no .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

Holm is not code-signed. Windows SmartScreen may warn on first launch; Smart App Control may block an unsigned app.

### Changed
- **AI usage, the Codenotch way.** The closed notch now carries a ring at each end – Claude on the left, Codex on the right – that fills and turns from green to yellow to red as a limit runs out, with a thin arc circling while an agent works. Hover or click a ring to open the notch on the new *AI usage* page: every limit with its reset time ("Resets in 51 min") and the sessions at work. Ported from [Codenotch](https://github.com/vinzdg/codenotch) by Vinz (MIT).
- **Claude's real limits.** Claude's percentages now come from Anthropic, the same numbers `/usage` shows, using the sign-in Claude Code already keeps on your PC. Holm only reads it and sends it nowhere but Anthropic, asks at most every five minutes, and never sends an expired sign-in. Without one, it falls back to the estimate from the logs and says so.

Holm 1.12.5 starts faster, needs fewer clicks for notes, reminders and the calculator, works better with the keyboard and screen readers, and explains Beam, invites and privacy more plainly. Part 1 of a leaner-and-faster pass.

## Download

| File | For whom |
| --- | --- |
| **Holm-1.12.5-win-x64.zip** | Unzip and run `Holm.exe`; also used by Holm's updater. |
| **Holm.exe** | Run the self-contained app directly; no .NET installation needed. |

Requires Windows 10 (2004 or newer) or Windows 11, x64.

Holm is not code-signed. Windows SmartScreen may warn on first launch; Smart App Control may block an unsigned app.

### Changed
- **Leaner and faster, part 1** (led by Djordje Mojsilovic, see `docs/opt/REPORT.md`; not finished yet). The notch is ready about a fifth sooner after start (median 1.56 → 1.27 s in 25 interleaved runs), because efficiency mode now starts once the notch is up, not during start-up. The Holm window's logo no longer carries a second copy of the 381 KB icon.
- **Fewer clicks.** Opening notes, reminders, the calculator or the weather place puts the cursor in its field; if you only look, the notch still closes when the mouse leaves. Enter in the profile's name or status applies it.
- **Keyboard and screen readers.** The Holm window's switches, sidebar and close button work with Tab and Space/Enter and say whether a switch is on; 21 icon-only notch buttons have names, and previous/play/next have tooltips.
- **Clearer texts.** Beam says *Couldn't reach …* instead of blaming a friend as offline and shows plain words when a connection drops; the invite page says that anyone with the code becomes your friend and that *New code* doesn't stop the old one; settings explain the weather location, what the clipboard history keeps and that Beam looks at copied text for codes; the phone link warns that it isn't encrypted; unreadable reminders say why.

### Fixed
- **Double clicks.** A double click on a reminder's × no longer removes the next reminder too, and a double click on *Accept* no longer cancels the transfer it just accepted.
- **Weather place search.** Only one search runs at a time, nothing is saved after you close the notch, and a failed forecast no longer asks you to choose a city again.
- **Speed test.** A ping that gets no answer gives up after 5 seconds instead of hanging.

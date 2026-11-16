# DECISIONS – Entscheidungen bei Unklarheit (sichere Option)

1. **Web-Begriffe auf Desktop übertragen.** Holm ist eine WPF Desktop App, keine Web App. Bundle = `Holm.exe`
   (self-contained single-file, wie Release), Route = Ansicht der Notch / Seite im Holm window, Lighthouse/API/DB =
   nicht anwendbar (kein Browser, keine eigene API, keine DB). Startzeit = Prozessstart bis erstes sichtbares Fenster.
2. **Website (`website/`) nicht im Scope.** Eigenes Deploy-Ziel (holmnotch.ch). Änderungen dort wären Deploy-relevant;
   der Auftrag spricht von "der App". Website bleibt unverändert.
3. **Dein laufendes Holm (`Downloads\Holm.exe`) wird nie beendet.** `tools/Measure-Footprint.ps1` beendet *alle*
   Holm-Prozesse; deshalb eigenes Skript `docs/opt/measure.ps1`, das nur selbst gestartete Prozesse stoppt.
4. **Testdaten statt Produktionsdaten.** Neuer Schalter `HOLM_DATA` (nur DEBUG- und BENCH-Builds, Release unverändert)
   leitet `%APPDATA%\Holm` auf einen Testordner um, nutzt einen eigenen Single-Instance-Mutex und überspringt die
   Autostart-Migration. Testdaten liegen in `out/opt-data/` (gitignored).
5. **Offline beim Messen.** Seed-Settings (`out/opt-data/settings.seed.json`): Update-Check aus, Wetter-Ortung aus,
   Beam aus, Freunde-Server und Relay auf `127.0.0.1:9`, Aktivität teilen aus, Passwort-Löschen aus (würde sonst
   deine echte Zwischenablage leeren), Musik-Pause bei Anrufen aus. Messwerte gelten für diese Konfiguration, vorher
   und nachher gleich.
6. **Live-Systemzustand.** Die Test-Instanz sieht deinen echten Mediaplayer, Benachrichtigungen und Zwischenablage
   (nur lesen, nur im Speicher). Laufende Musik kann Idle-CPU verändern (Equalizer). Wird im Bericht als Grenze genannt.
7. **`graphify-out/` nicht committet.** Untracked Ausgabe eines Tools (21 MB), nicht Teil der App. Bleibt unangetastet.
8. **Branch-Basis.** `opt/lean` und Tag `opt/start` zeigen auf `780965e` (Stand von `test/release-asset-names`, 1 Commit
   vor `main`), weil das der Ausgangsstand im Arbeitsordner war.
9. **Mobile-Screenshots.** Kein Mobile. Ersatz: Notch-Grössen (Small … Extra large) wo relevant; `--snap` rendert
   Grösse Normal.
10. **Roaster ohne Live-Zugriff.** Subagents können die WPF App nicht bedienen (kein Browser, Single-Instance, Maus des
    Nutzers). Roaster arbeiten mit Code und `--snap` Screenshots. Jeder Bericht trägt "nicht live getestet".
11. **Advisor-Zeitpunkte** wie im Auftrag: nach Phase 2, 3, 7 und vor Phase 10.
12. **`docs/opt/` und `website.yml`.** Der Website-Workflow läuft bei Änderungen unter `docs/**` (die Website nutzt
    `docs/*.png`). Ein Merge dieses Branches baut die Website also neu (gleicher Inhalt). Workflow bleibt unverändert,
    weil Website out of scope ist (2). Wer das vermeiden will: `docs/opt/` vor dem Merge entfernen oder Filter anpassen.
13. **Autofocus in Tool-Seiten (Verhaltensänderung, bewusst).** Klick auf Notiz/Erinnerung/Rechner (oder Wetter-Ort)
    setzt den Cursor ins Feld. Vorher brauchte es einen zweiten Klick ins Feld. Sicherung: Ohne Tastendruck schliesst die
    Notch beim Wegfahren der Maus wie bisher (`KeepOpenForTyping`), Esc gibt die Tastatur zurück, Klick in andere App
    ebenso. Der Fokus wird nur nach einem Klick in Holm genommen (Windows erlaubt Vordergrund nur dann). README angepasst.
14. **Ziele nach verworfenem Composite-Schalter neu.** GOALS.md hatte Exe ≤ 165 MB / Private ≤ 135 MB aus der
    Variante ohne Composite. Diese ist verworfen (Start +24 %). Neue Ziele leiten sich aus Ausschlüssen + Icon ab
    (siehe GOALS.md, Abschnitt "Neu ab Welle 1").
15. **Regel 12 offen ausgewiesen.** Composite-Roots: 3 Fehlversuche → verworfen. Composite-Ausschlüsse: 4 Publish-Versuche
    (Pfad-Items wirkungslos, Fassaden fehlten, MSBuild-Syntax, dann explizite Liste erfolgreich) – zählt als neue,
    jeweils andere Variante; dokumentiert in PLAN.md.
16. **Bestehende UI-Tests lesen echte Settings.** `UpdateMenuTests` bauen `MainWindow` mit `AppSettings.Load()` aus
    `%APPDATA%\Holm`, ohne `ReadOnly`. Nicht geändert (vorbestehend, Regel: nur notieren). Neue Tests setzen `ReadOnly`.

# REPORT – Holm schlanker und schneller, Teil 1 (nicht fertig)

## a. Zusammenfassung (5 Sätze)

Holm.exe ist um 9,2 MB kleiner (213,2 → 204,0 MB), weil 44 nie geladene Framework-Assemblies nicht mehr ins
R2R-Composite kompiliert werden und das Icon nicht mehr doppelt im Exe liegt. Die Notch ist nach dem Start rund 19 %
früher bedienbar (Median 1561 → 1269 ms, 25 abwechselnde A/B-Paare), weil der Effizienzmodus erst nach dem Start greift.
Notiz, Erinnerung, Rechner und Wetter-Ort brauchen je einen Klick weniger, das Holm-Fenster ist per Tastatur und
Screenreader bedienbar, und zehn Fehler- und Datenschutztexte sagen jetzt die Wahrheit. Die UX-Gesamtnote der zehn
Personas stieg nur von 4,9 auf 5,3 (Ziel war +1,0); acht Punkte mit Schwere 5 bleiben offen, meist neue Funktionen
(globale Hotkeys, Übertragung fortsetzen). Auf Wunsch des Nutzers wurde nach Teil 1 abgebrochen: Endmessung, sauberer
Install und Advisor-Abschlussprüfung fehlen (siehe h).

## b. Zahlen

| Wert | Vorher | Nachher | Ziel | Methode |
|---|---|---|---|---|
| Holm.exe (Release) | 212,96 MB | ~204,0 MB¹ | ≤ 205 MB | Publish wie release.yml |
| Composite-Image im Exe | 122,0 MB | 96,4 MB | – | Bundle-Extraktion |
| Bedienbar nach Start | 1561 ms | **1269 ms** | besser | A/B 25 Paare, 18/25 schneller |
| CPU erste 11 s | 5453 ms | **4875 ms** | ≤ vorher | A/B 25 Paare, 20/25 weniger |
| JIT-Methoden bis bedienbar | 633 | 634 | gleich | ready.txt |
| Private Memory nach 11 s | 139 MB | 139 MB | ≤ vorher | A/B |
| Abhängigkeiten (NuGet) | 2 | 2 | ≤ 2 | csproj |
| Tests | 1020 | 1023 (+3 neue) | grün | gate.ps1 |
| F2 Notiz / F3 Erinnerung / F5 Rechner | 3 / 3 / 4 Klicks | 2 / 2 / 3 | −1 | Code |
| F16 Wetter-Ort | 4 Klicks | 3 | −1 | Code |
| F21 Profil | nur Klick | Enter = Apply | Tastatur | Code |
| Tastatur-erreichbare Einstellungen | 0 | alle | – | Code |
| Benannte Icon-Buttons (Screenreader) | 0 | 21 + alle Schalter | – | Code |
| UX Ø Gesamtnote | 4,9 | 5,3 | +1,0 | 10 Haiku-Personas |

¹ Release-Exe nach allen Code-Änderungen ausser den letzten Texten: 213 751 317 Byte (203,85 MB). Die finale Messung
(measure-after.json) wurde zweimal von Smart App Control blockiert und dann auf Wunsch abgebrochen.

## c. Änderungen nach Kategorie (je eigener Commit, `git log opt/start..opt/lean`)

- **Grösse:** Composite-Ausschlüsse (9055efc), Logo als 3-KB-PNG (d524216).
- **Start:** EcoQoS erst nach dem Start (1552b1b).
- **Abläufe:** Autofocus Tool-Felder (248ee3c), Wetter-Feld (6bd560a), Enter im Profil (7284aa4).
- **Accessibility:** 21 Namen + Tooltips (27f19d1), Tastatur im Holm-Fenster (302cb48), Schalterzustand (530c8cd).
- **Fehlertoleranz:** Doppelklick-Schutz Erinnerung (d0be8fa) und Accept (0fa7791), Wettersuche (b738a25, 02ed647),
  Speedtest-Ping-Timeout (1127d7b).
- **Vertrauen/Texte:** Beam-Meldungen (0fa7791), Einladung (0d88f68, ad8bbc0), Settings-Texte (7c19bcd, d45d24c),
  Phone-Warnung (662fbe9).
- **Infrastruktur (nur Debug/Bench):** HOLM_DATA-Testordner (2a92abd), Ready-Stempel (6cbf7c3).

## d. Roast-Noten Runde 1 → Runde 2

| | Verständl. | Tempo | Vertrauen | Optik | Fehlertol. | Gesamt |
|---|---|---|---|---|---|---|
| Runde 1 | 5,9 | 5,6 | 5,0 | 7,5 | 4,6 | **4,9** |
| Runde 2 | 6,0 | 5,4 | 5,1 | 7,5 | 4,9 | **5,3** |

Punkte mit Schwere ≥ 4: 27 → 27 (Schwere 5: 9 → 8). Grösste Sprünge: Erstnutzer 5 → 7, Chaot 4 → 6.
Runde 2 fand teils neue, tiefere Punkte (Roaster sind nicht deterministisch, nur Code und Screenshots).

## e. Top-5-Kritik vorher → nachher

| Vorher (Runde 1) | Nachher |
|---|---|
| Notch/Settings nicht per Tastatur (P4, Schwere 5) | Holm-Fenster bedienbar; Notch selbst noch nicht (Hotkey fehlt) |
| Icons ohne Namen/Tooltips (6 Personas) | 21 Namen + Tooltips; sichtbare Beschriftung fehlt weiter |
| Doppelklick löscht zwei Erinnerungen | behoben |
| "X is offline" bei eigenem schlechtem Netz | behoben |
| Fällige Erinnerung geht bei Klick ausserhalb verloren (Schwere 5) | offen (Produktentscheid nötig) |

## f. Bewusst nicht geändert

Composite ganz aus (−56 MB, aber Start +24 %); Composite-Roots (SDK-Absturz, 3×); Equalizer/fps (Kernmerkmal);
Apply-Semantik (gewollt); Speedtest-Autostart (Datenverbrauch); Protokoll für Abbruch/Resume (Schnittstelle, Regel 10);
Website (out of scope); Theme-DLLs (High-Contrast-Fallback). Details: PLAN.md, TRIAGE.md, DECISIONS.md.

## g. Offene Risiken und Backlog

- Hoch: fällige Erinnerung verloren; globale Hotkeys/Tray (Tastatur-Einstieg); Beam-Resume und Stillstand-Timeout;
  Phone-Link per HTTP (nur Warnung ergänzt); Clipboard-Bilder in %TEMP% bei Absturz.
- Mittel: Slider-Trefferfläche, Notch-Grösse in Settings, Tab-Reihenfolge Apply, Chat-Liste per Tastatur, Esc im Chat
  verwirft Entwurf, "New code" widerruft nicht, Wetter-Treffer mehrdeutig, Reminder-Overflow bei Riesenzahlen.
- Messung: Holm-Fenster +67 MB nativ beim ersten Öffnen (Einmal-Ladung, kein Leck).
- Bestehende UI-Tests lesen echte Settings (DECISIONS 16).

## h. Grenzen

- **Nichts live von Personas getestet:** Roaster arbeiteten nur mit Code und `--snap`-Screenshots.
- **Abgebrochen nach Teil 1:** keine finale measure.ps1 (Smart App Control blockierte die neue Bench-Exe 2×), kein
  sauberer Install mit anschliessendem Voll-Gate, kein Advisor vor Abschluss. Das letzte Gate (Build, 1023 Tests,
  70-Bilder-Smoke) lief grün auf dem Stand vor den CHANGELOG/CONTRIBUTORS-Änderungen.
- Messrauschen hoch (Nutzer am PC, Musik); nur A/B-Paare sind belastbar. Idle-CPU nicht stabil messbar.
- Wichtig: Teile 8 parallel von Fix-Agents (je eine Datei) umgesetzt, auf Wunsch des Nutzers; jede Änderung durchs Gate.

## i. Rückweg

`git checkout opt/start` (Ausgangsstand) oder einzelne Commits mit `git revert <hash>`. Nichts gepusht, nichts gemergt.

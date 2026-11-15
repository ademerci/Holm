# Persona 10 – Altes Gerät, langsames Netz

"nicht live getestet"

Code und Screenshots gelesen. Keine Messung auf Dual-Core/4 GB; alle Werte aus dem Code abgeleitet.

| ID | Ort | Was passiert | Beleg | Schwere (1-5) | Wie oft | Verbesserungsvorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| ANI-1 | F1, Notch bei Musik | Reaktiver Equalizer standardmäßig an: 25-fps-Timer + Loopback-Audio-Thread, solange Musik spielt. | `Settings.cs:15`; `MainWindow.xaml.cs:742-745`, `:711-712`; `AudioMeter.cs:133-139`; `README.md:157-162` | 4 | immer (bei Musik) | Auf schwachen Geräten aus oder fragen; 10–12 fps. | mittel |
| WX-1 | F16, Wetter-Ort suchen | Nach Enter nur "Looking for X…"; kein Spinner, Feld editierbar, zweites Enter startet zweite Suche; bis 20 s. | `MainWindow.Weather.cs:176-182`, `:92`, `:185` | 4 | oft | Flag `_searching`, Feld sperren, "Looking… Esc to cancel". | hoch |
| MED-1 | F1, Albumcover | Titel/Interpret erst nach Cover-Dekodierung veröffentlicht; kein Timeout. | `MediaService.cs:259-269`, `:350-371`, `:367`, `:402`; `MainWindow.xaml.cs:1146` | 3 | oft | Titel sofort, Cover nachreichen; Timeout ~2 s. | mittel |
| WX-2 | F16, Abbrechen | Esc schliesst die Notch, bricht die Suche nicht ab; Ort wird später still gespeichert. | `MainWindow.Tools.cs:107-110`; `MainWindow.Weather.cs:183-197` | 3 | selten | Suchtoken halten, bei Esc/Schliessen abbrechen. | hoch |
| WX-3 | F16, Fehlerzustand | Nach fehlgeschlagener Abfrage "Weather unavailable · choose city", obwohl Ort gespeichert; Retry erst nach 5 min. | `MainWindow.Weather.cs:124-125`, `:107` | 3 | oft | "Can't reach weather · retrying in 5 min". | hoch |
| SPD-1 | F19, Ping | Kein Zeitlimit für Ping; "Measuring ping…" hängt bis Stop. | `SpeedTest.cs:49`, `:78`; `NetworkPage.cs:90`, `:183`; `README.md:36` | 3 | selten | CancelAfter ~5 s pro Ping. | mittel |
| SPD-2 | F19, Upload-Ergebnis | Upload zählt nach `WriteAsync`, nicht nach Server-Empfang; Vermutung. | `SpeedTest.cs:261-262`, `:109-112` | 3 | immer | Über Request-Ende messen. | niedrig |
| ANI-2 | F1, Einstellung finden | Animationen/Equalizer-Schalter nur im Einstellungsfenster. | `MainWindow.xaml:262-263`; `MainWindow.Settings.cs:21` | 2 | immer | "Weniger Bewegung" im Rechtsklick-Menü. | hoch |
| LOC-1 | F16, Auto-Standort | Ortung fehlgeschlagen/12 s: nur "Choose weather city", ohne Grund. | `MainWindow.Weather.cs:54`, `:70-71`, `:123-124` | 2 | selten | Grund anzeigen. | hoch |
| MEM-1 | F1, Speicher auf 4 GB | Trim beim Schliessen, Wiederaufbau beim Öffnen; nicht gemessen. | `MainWindow.Motion.cs:90-94`; `MainWindow.xaml.cs:895-896`; `README.md:162` | 2 | immer | Trim seltener. | niedrig |

Nicht als Mangel gemeldet: 140-ms-Hover-Verzögerung; 7-Sekunden-Phasen mit Obergrenze.

Nicht geprüft: CPU/RAM auf Dual-Core, Netzhänger, Cover-Wartezeit, Wettrennen zweier Suchen, Upload-Verzerrung. Kein Screenshot mit Wartezustand.

Noten:
Verständlichkeit: 6
Tempo: 4
Vertrauen: 4
Optik: 7
Fehlertoleranz: 4
Gesamt: 5

Behalten: Der Speedtest startet nur auf Klick und begrenzt jede Phase auf 7 Sekunden und 300 MB, und wer Animationen ausschaltet, schaltet damit auch den Equalizer und die Cover-Überblendung ab. Das ist ehrliches, schlankes Verhalten und sollte bleiben.

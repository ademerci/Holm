# Persona 10 – Altes Gerät, langsames Netz (Runde 2)

"nicht live getestet"

| ID | Ort | Was passiert | Beleg | Schwere | Wie oft | Vorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| F19-01 | Wi-Fi, Ping-Phase | Bis 9 × 5 s nur "Measuring ping…"; kein Zähler; "about 15 seconds" versprochen. | `SpeedTest.cs:40, 76, 81`; `NetworkPage.cs:90, 171, 180` | 4 | oft | Zähler, Gesamtlimit, ehrlicher Startsatz. | hoch |
| F1-01 | Notch-Animationen/EQ | 520 ms Öffnen, 700 ms Überblendung, EQ 25 fps; keine Erkennung schwacher Hardware, Windows-Animationsoption ignoriert. | `MainWindow.Motion.cs:48`; `MainWindow.xaml.cs:698, 711-712, 742`; `Settings.cs:13, 15` | 4 | immer | Auf schwacher Hardware aus; ClientAreaAnimation respektieren. | niedrig |
| F16-01 | Wetter-Suche Wartezustand | Nur statischer Text; zweites Enter still ignoriert. | `MainWindow.Weather.cs:183-186`; `Weather.cs:39` | 4 | oft | Spinner, "Still looking…". | hoch |
| F16-03 | Wetter-Suche, Fensterwechsel | Ergebnis still verworfen. | `MainWindow.Tools.cs:145-149`; `MainWindow.xaml.cs:116-120, 429`; `MainWindow.Weather.cs:190` | 3 | oft | Hinweis beim nächsten Öffnen. | mittel |
| F19-03 | Wi-Fi Stop/Fehler | Zwischenstand verschwindet; präzise Meldung verworfen. | `NetworkPage.cs:149, 154, 159`; `SpeedTest.cs:85` | 3 | oft | Zwischenstand + ex.Message. | hoch |
| F16-05 | Wetter "retrying soon" | Retry real nach 5–35 min. | `MainWindow.Weather.cs:30, 107, 125`; `Weather.cs:143` | 3 | oft | Ehrlicher Text oder 5-min-Timer. | hoch |
| F16-04 | Wetter nach Auswahl | Bis ~30 s Wartezeit, nur kleiner Text. | `MainWindow.Weather.cs:120-125, 196-201` | 3 | oft | Ladestatus grösser. | mittel |
| F16-02 | Wetter "(Esc to cancel)" | Esc schliesst Notch, Suche läuft weiter (Ergebnis verworfen). | `MainWindow.Weather.cs:184, 190`; `MainWindow.Tools.cs:107-110` | 2 | selten | Text ändern oder Cancel. | hoch |
| F1-02 | Albumcover | Grauer Block bei Dekodierfehler, still. | `MainWindow.xaml.cs:1146`; `MediaService.cs:258-263, 350-370` | 2 | oft | Fallback mit Initialen. | mittel |
| F19-02 | Wi-Fi Live-Anzeige | Startet mit "0.00 Mbps". | `NetworkPage.cs:176, 180`; `SpeedTest.cs:39, 166-169` | 2 | oft | Erst "–". | mittel |

Noten:
- Verständlichkeit: 6
- Tempo: 4
- Vertrauen: 5
- Optik: 7
- Fehlertoleranz: 5
- Gesamt: 5

Behalten: Die Cover-Dekodierung ausserhalb des UI-Threads und der Animations-Schalter als Notausgang tragen auf schwacher Hardware.

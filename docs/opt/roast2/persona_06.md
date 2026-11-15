# Persona 6 – Chaot (Runde 2)

nicht live getestet

| ID | Ort | Was passiert | Beleg | Schwere | Wie oft | Vorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P1 | Fällige Erinnerung | Beim Auslösen aus Liste gelöscht; ohne OK/+10 nach 60 s weg. | `MainWindow.Reminders.cs:35, :62-65`; `Reminders.cs:166-167` | 4 | oft | In Liste bis OK / "verpasst". | hoch |
| P2 | Erinnerung, Riesenzahl | "300000000 h" → OverflowException, still geschluckt. | `Reminders.cs:70-77`; `MainWindow.Reminders.cs:84, :95`; `App.xaml.cs:15-19` | 3 | selten | Obergrenze vor TimeSpan prüfen. | hoch |
| P3 | Erinnerung, falsche Meldung | "25:99"/"200 h" erkannt, aber abgelehnt; Meldung sagt "Couldn’t find a time". | `Reminders.cs:96`, `:77`; `MainWindow.Reminders.cs:97` | 2 | oft | Meldung je Grund. | hoch |
| P4 | Wetter, mehrdeutige Namen | Erster Treffer ohne Auswahl; Zeile zeigt nur Stadt. | `Weather.cs:44`; `MainWindow.Weather.cs:132, :150` | 3 | selten | Region/Land bestätigen. | hoch |
| P5 | Wetter, Enter während Suche | Stumm ignoriert; bis 20 s. | `MainWindow.Weather.cs:183, :185` | 3 | oft | Laufende Suche ersetzen. | hoch |
| P6 | Wetter-Hinweis | "Now: Munich…" ohne "Enter to change". | `after/expanded-weather-place.png`; `MainWindow.Weather.cs:171` | 2 | immer | Hinweis erweitern. | hoch |
| P7 | Beam, Drop während Übertragung | Still verworfen. | `MainWindow.Share.cs:222`, `:167-169` | 2 | oft | Peek "Busy". | hoch |
| P8 | Beam, ignorierter Empfang | Nach 60 s als "declined" gemeldet. | `MainWindow.Share.cs:805-806, :493`; `Sharing\FileTransfer.cs` | 2 | oft | "No answer". | mittel |
| P9 | Beam, harter Kill | Halbe Datei im Downloads-Ordner. | `Sharing\FileTransfer.cs:173-176`, `:213-216` | 3 | selten | `.part`-Datei. | mittel |
| P10 | Esc während Übertragung | Keine Wirkung. | `MainWindow.Tools.cs:105-107`; `MainWindow.Share.cs:827` | 1 | oft | Esc = Abbrechen? | mittel |

Noten:
- Verständlichkeit: 7
- Tempo: 7
- Vertrauen: 4
- Optik: 8
- Fehlertoleranz: 5
- Gesamt: 6

Behalten: Die Beam-Abbruchkette und der Doppelklick-Schutz bei Accept und beim Entfernen von Erinnerungen.

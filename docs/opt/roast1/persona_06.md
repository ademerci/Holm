# Persona 6 – Chaot

nicht live getestet

Alle Punkte stützen sich auf Code und Screenshots.

| ID | Ort | Was passiert | Beleg | Schwere (1-5) | Wie oft | Verbesserungsvorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| F3-1 | Erinnerung, Pop-out bei Fälligkeit | Fällige Erinnerung nach 60 s weg; Klick ausserhalb schliesst sie sofort; schon aus der Liste gelöscht. Mehrere fällig: nur die erste gezeigt. | `MainWindow.Reminders.cs:48-64`; `MainWindow.ClickAway.cs:20, 64`; `Reminders.cs:164-168` | 5 | oft | Erst nach OK/+10 min aus Liste nehmen; Klick ausserhalb ≠ Bestätigung; alle fälligen zeigen. | hoch |
| F3-2 | Erinnerungsliste, Zeilen-× | Doppelklick auf × löscht zwei Erinnerungen (nächste rutscht nach). Kein Rückgängig. | `MainWindow.Reminders.cs:99-101, 117-126` | 4 | oft | Kurze Sperre oder Rückgängig. | mittel |
| F3-3 | Erinnerung während Beam/Code-Warten | Nur 10-s-Peek ohne Buttons; vorher schon gelöscht. Code-Beam hält `_busy` bis 15 min. | `MainWindow.Reminders.cs:56-60`; `Reminders.cs:166-167`; `MainWindow.Share.cs:458, 820` | 4 | selten | In Liste lassen bis bestätigt. | hoch |
| F3-4 | Erinnerungseingabe | "asdf", "25:99 dentist", >200 Zeichen: Enter tut nichts; Hinweis nur "Add a time". | `MainWindow.Reminders.cs:86, 94`; `Reminders.cs:52, 96` | 2 | oft | Grund nennen. | hoch |
| B-1 | Beam, Abbruch durch Empfänger | Kein Abbruch-Frame; Sender sieht rohen Systemtext. | `Sharing\FileTransfer.cs:213-217`; `Sharing\SecureChannel.cs:41-42`; `MainWindow.Share.cs:895` | 3 | oft | Abbruch-Frame; Meldungen übersetzen. | mittel |
| B-2 | Beam, Abbruch durch Sender | Kein Abbruch-Signal in Datenschleife; Empfänger sieht "Couldn't receive – The connection was closed". | `Sharing\FileTransfer.cs:80-94`; `MainWindow.Share.cs:772-776, 894`; `SecureChannel.cs:45, 50` | 3 | oft | Abbruch-Frame, "Sender cancelled". | mittel |
| F11-3 | Beam, Annahme-Pop-out | Cancel erscheint fast an Accept-Position; Doppelklick bricht Empfang ab. | `MainWindow.Share.cs:800-807`; `MainWindow.xaml:515-516`; `airdrop-ask.png`, `airdrop-progress.png` | 3 | oft | Accept sperren; Cancel verzögert zeigen. | niedrig |
| F16-2 | Wetter, Ortswahl | Erster Geocoding-Treffer ohne Rückfrage gespeichert; Notch zeigt nur Teil vor dem Komma. | `Weather.cs:44`; `MainWindow.Weather.cs:132, 150, 169, 186-191` | 3 | selten | Vollen Namen zeigen oder Trefferliste. | mittel |
| F16-3 | Wetter, Esc während Suche | Esc schliesst, Suche speichert im Hintergrund weiter. | `MainWindow.Tools.cs:105-111`; `MainWindow.xaml.cs:429`; `MainWindow.Weather.cs:186-197` | 2 | oft | Suche abbrechen bei Esc. | mittel |
| F16-4 | Wetter, kein Netz | "Weather unavailable · choose city" – gibt der Stadt die Schuld. | `MainWindow.Weather.cs:104-108, 123-125` | 2 | oft | "Keine Verbindung – erneut versuchen". | hoch |

Noten:
- Verständlichkeit: 5
- Tempo: 6
- Vertrauen: 3
- Optik: 8
- Fehlertoleranz: 3
- Gesamt: 4

Nicht geprüft: Open-Meteo bei Tippfehlern, Doppelklick-Timing in WPF, Abbruch im Relay-Pfad, Netz-Timeouts, Verhalten nach Neustart mit mehreren fälligen Erinnerungen.

Behalten: Die Live-Vorschau der Erinnerungseingabe ("Call Lena · in 27 min · Enter to add", `expanded-reminders.png`) zeigt vor dem Enter, wie die App den Text verstanden hat.

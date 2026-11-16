# Persona 3 – Unterwegs-Nutzer (Runde 2)

nicht live getestet

| ID | Ort | Was passiert | Beleg | Schwere | Wie oft | Vorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P-01 | F11 Verbindungsverlust | "The connection dropped – try again."; Teildatei gelöscht, Neustart bei 0. | `Sharing\FileTransfer.cs:213-217`; `MainWindow.Share.cs:898`; `airdrop-progress.png` | 5 | oft | Resume ab Offset / Stand im Text + "Erneut senden". | hoch |
| P-02 | F11 hängende Übertragung | Kein Stillstand-Timeout, nur Cancel. | `Sharing\FileTransfer.cs:61, 88, 190`; `Sharing\RelayLink.cs:35`; `MainWindow.Share.cs:867-873` | 4 | oft | Stillstand-Timer 20 s. | mittel |
| P-03 | F19 Ergebnis | Bei Fehler/Stop verschwinden Ping und Download; Fehler klein grau. | `NetworkPage.cs:149-173`, `:181`, `:26`; `SpeedTest.cs:147` | 4 | oft | Zwischenstand behalten, Fehler rot. | hoch |
| P-04 | F11 Anfrage-Fehler | IOException beim Anfragen → "The connection dropped" (Ursache falsch), 5 s. | `FriendsService.cs:462-466`; `Friends.cs:481-487`; `MainWindow.Share.cs:484, 516, 881, 892-901` | 3 | oft | "Kein Kontakt zum Server", 8 s. | hoch |
| P-05 | F19 Ping | 9 × 5 s; ein Timeout bricht alles ab; "about 15 seconds" nur bei gutem Netz. | `SpeedTest.cs:40, 76-85`; `NetworkPage.cs:90` | 3 | oft | Ausreisser verwerfen, einmal wiederholen. | mittel |
| P-06 | F11 grüner Punkt | Nach Fehlschlag bleibt Freund bis 2 min "online". | `MainWindow.Share.cs:502, 505-512, 332`; `FriendsService.cs:519, 760, 766` | 3 | oft | MarkOffline auch im Send-later-Zweig. | mittel |
| P-07 | F11 Fortschritt | Keine Geschwindigkeit/Restzeit (Downloads haben sie). | `MainWindow.Share.cs:873`; `MainWindow.Downloads.cs:103-106` | 3 | immer | Rate wiederverwenden. | hoch |
| P-08 | F1 Lautstärke Touchpad | 84 DIP breit, 16 hoch, Spur 4; kein Mausrad. | `MainWindow.xaml:915`; `Controls.cs:76-77, 84` | 3 | oft | 140 DIP, Mausrad. | hoch |
| P-09 | F1 Vor/Zurück | Keine sofortige Rückmeldung; Fehler verschluckt. | `MainWindow.xaml.cs:1370-1377`; `MediaService.cs:298-302` | 3 | immer | Optimistisch wie Play. | hoch |
| P-10 | F11 Einstieg | Nur Drag; kein "Send file…" im Menü. | `MainWindow.xaml:227, 232-297`; `ChatPage.cs:197` | 3 | oft | Menüeintrag. | mittel |

Noten (1-10):
- Verständlichkeit: 7
- Tempo: 6
- Vertrauen: 4
- Optik: 8
- Fehlertoleranz: 3
- Gesamt: 5

Behalten: den 140-ms-Hover mit den grossen Transporttasten und die "Send later"-Warteschlange, die bei einem offline Freund wartet, statt zu scheitern.

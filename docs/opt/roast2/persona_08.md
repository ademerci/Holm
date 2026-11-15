# Persona 8 – Konkurrenz-Vergleicher (Runde 2)

nicht live getestet

| ID | Ort | Was passiert | Beleg | Schwere | Wie oft | Vorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| F13-01 | Screenshot-Pop-out | Vorschaubild nicht ziehbar. | `MainWindow.xaml:502-503`; `after/screenshot.png`; `MainWindow.Shelf.cs:116-149` | 4 | oft | Drag-Quelle. | hoch |
| F1-01 | Musik, Songwechsel | Kein Pop-out bei neuem Titel. | `MainWindow.xaml.cs:889-918`; `settings-popouts.png` | 3 | oft | Optionaler Peek. | hoch |
| F11-01 | Beam-Code | 15-min-Ablauf nicht angezeigt. | `MainWindow.Share.cs:454`, `:468`; `airdrop-code.png` | 3 | oft | "gilt 15 min". | hoch |
| F11-02 | Codeformate | `WN-…` vs `HOLM-…`. | `MainWindow.Share.cs:451`; `AddFriendPage.cs:76`; `MainWindow.Invites.cs:83` | 3 | oft | Ein Format. | hoch |
| F13-02 | Screenshot-Pop-out | Still unterdrückt bei Übertragung/Anruf/Vollbild; 6 s. | `MainWindow.Screenshot.cs:122`, `:133` | 3 | oft | Nachreichen. | hoch |
| F13-03 | Clipboard-Screenshots | Nur Snipping Tool erkannt. | `MainWindow.Screenshot.cs:21-22`, `:61-65` | 3 | oft | Erweiterbar. | mittel |
| F11-03 | Beam empfangen | 60 s Frist ohne Anzeige. | `MainWindow.Share.cs:795-806`, `:437` | 2 | selten | Frist anzeigen. | hoch |
| F13-04 | Screenshot Send | Picker schliesst nach 900 ms ohne Maus. | `MainWindow.Screenshot.cs:221-233`, `:35`; `MainWindow.xaml.cs:1337-1341` | 2 | oft | Länger offen. | hoch |
| F1-02 | Musik, Spulen | Leiste 4/7 px, keine Zeitvorschau. | `MainWindow.xaml.cs:115`, `:152-159`; `Controls.cs:84`, `:77` | 2 | oft | Zeitvorschau. | mittel |
| F1-03 | Musik-Peek | Nur Cover und Equalizer. | `after/peek.png` | 2 | oft | Titel zeigen. | hoch |

Noten 1-10:

Verständlichkeit: 7
Tempo: 8
Vertrauen: 6
Optik: 8
Fehlertoleranz: 5
Gesamt: 7

Behalten: Die Kette aus Pop-out mit Send und Shelf direkt nach dem Screenshot, zusammen mit Beam per Freund ohne Code und ohne Passwortweitergabe, ist das, was kein Konkurrent in Windows so bündelt.

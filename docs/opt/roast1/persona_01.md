# Persona 1 – Ungeduldiger Erstnutzer

"nicht live getestet"

Geprüft wurden Screenshots und Code. Die Schritte selbst habe ich nicht ausgeführt. Ob Musik läuft, wie schnell der Hover reagiert und wie sich die Pop-outs anfühlen, ist nicht geprüft.

| ID | Ort | Was passiert | Beleg | Schwere (1-5) | Wie oft | Verbesserungsvorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P1 | F1, Notch im Ruhezustand | Der Nutzer sieht nur die Uhr. Pause ist erst nach Hover (140 ms) oder Klick sichtbar. Im Compact-Zustand gibt es keine Steuerung, nur Cover und Equalizer. | `docs\opt\before\idle.png` (nur "18:03"); `compact.png` (ohne Steuerung); `WinNotch\MainWindow.xaml.cs:115` (140 ms), `:1344-1368` (Klick) | 4 | oft | Im Compact-Zustand eine Pause-Taste zeigen, oder mindestens einen Hinweis, dass ein Klick öffnet. | hoch |
| P2 | F1, Play-Button | Der Play/Pause-Button hat keinen Tooltip. Prev und Next haben auch keinen, Repeat dagegen schon. | `WinNotch\MainWindow.xaml:632-637` (Prev, Play, Next ohne ToolTip), `:642` (Repeat mit `ToolTip="Repeat"`) | 3 | immer | ToolTips für Prev, Play/Pause und Next setzen, passend zu Repeat. | hoch |
| P3 | F15, Animationen: zwei Wege | Im Holm-Fenster wird der Schalter erst nach Apply gespeichert. Im Rechtsklick-Menü wirkt derselbe Schalter sofort. Der Nutzer erwartet bei beiden dasselbe. | `WinNotch\SettingsWindow.cs:431-452`; `WinNotch\MainWindow.xaml.cs:1616-1622`; `WinNotch\MainWindow.xaml:262`; `settings.png` | 3 | selten | Im Holm-Fenster den Schalter direkt wirken lassen, oder im Menü denselben Stand wie das Fenster zeigen. | mittel |
| P4 | F3, Erinnerung finden | Die Erinnerung öffnet sich nur über ein unbeschriftetes Glocken-Icon in der Leiste. Der Name steht nur im Tooltip. | `expanded.png` (sechs Icons ohne Text); `WinNotch\MainWindow.xaml:540` (nur ToolTip "Reminders") | 3 | oft | Kurze Beschriftung unter dem Icon, oder das Eingabefeld direkt im Hauptbereich zeigen. | hoch |
| P5 | F3, Reminder beim beschäftigten Notch | Ist der Notch gerade beschäftigt, kommt die Erinnerung nur als 10-Sekunden-Peek ohne "OK" und "+10 min". Der Pop-out verschwindet sonst nach 60 s. | `WinNotch\MainWindow.Reminders.cs:56-60`, `:63` | 3 | selten | Erinnerung in der Liste bleiben lassen, bis sie bestätigt ist, und auch im Peek eine Aktion anbieten. | hoch |
| P6 | F3, Eingabe | Die Hinweiszeile ist klein und grau. Ungültige Eingaben werden mit Enter stumm abgefangen. | `WinNotch\MainWindow.Reminders.cs:94`, `:86`; `expanded-reminders.png` | 2 | oft | Fehlerhinweis bei Enter in Rot oder kurz aufblinkend zeigen. | hoch |
| P7 | F15, Beschreibung des Schalters | "Smooth transitions and the moving equalizer." sagt nicht, dass auch GIFs, der Himmel und die Heute-Karten stillstehen. | `WinNotch\MainWindow.Settings.cs:20`; `ChatPage.cs:57`, `MainWindow.Sky.cs:96`, `MainWindow.Today.cs:62` | 2 | selten | Beschreibung um "GIFs and the sky" ergänzen. | mittel |
| P8 | Erstkontakt, Ruhezustand | Die Notch zeigt nur eine Uhr. Kein Hinweis, dass man mit der Maus hineingeht oder das Rechtsklick-Menü nutzt. Onboarding nicht geprüft. | `idle.png` | 2 | immer | Beim ersten Start einen einmaligen Hinweis zeigen. | niedrig |
| P9 | F15, Schliessen | Esc ist im Settings-Fenster behandelt. Ob es das Fenster schliesst, nicht geprüft. | `WinNotch\SettingsWindow.cs:230` | 1 | selten | Nicht geprüft. | niedrig |

**Noten (1-10)**

- Verständlichkeit: 5
- Tempo: 6
- Vertrauen: 6
- Optik: 8
- Fehlertoleranz: 5
- Gesamt: 5

**Behalten:** Die Reminder-Zeile mit dem Platzhalter "20 min laundry · 14:30 dentist" und der Live-Vorschau "Call Lena · in 27 min · Enter to add" (`expanded-reminders.png`) erklärt die Funktion, ohne dass man etwas lesen muss.

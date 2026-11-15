# Persona 1 – Ungeduldiger Erstnutzer (Runde 2)

"nicht live getestet"

| ID | Ort | Was passiert | Beleg | Schwere | Wie oft | Vorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P1 | Notch-Werkzeugleiste (F3, F15) | Glocke und Mond nackte Icons ohne Beschriftung; Name nur im Tooltip. | `after/expanded.png`; `MainWindow.xaml:540-542` | 3 | oft | Beschriftung oder Hinweis-Chip beim ersten Öffnen. | hoch |
| P2 | Kompakte Notch (F1) | Kein Play/Hinweis, dass Hover/Klick öffnet. | `after/compact.png`; `MainWindow.xaml.cs:115`, `:1344-1367` | 3 | oft | Chevron/Play im Pill. | mittel |
| P3 | Holm-Fenster, General (F15) | Animations-Schalter sauber, aber Menüeintrag versteckt. | `after/settings.png`; `MainWindow.xaml:259`; `SettingsWindow.cs:483-488` | 2 | selten | Hinweis im Rechtsklick-Menü. | hoch |
| P4 | Apply (F15) | Umgelegter Schalter sagt nicht "noch nicht gespeichert". | `SettingsWindow.cs:303-306`, `:241`; `after/settings.png` | 2 | oft | "1 change to apply" neben Apply. | mittel |
| P5 | Erinnerungen (F3) | Hinweis nur Englisch, Deutsch wird nicht erwähnt. | `MainWindow.Reminders.cs:83,87,97`; `MainWindow.xaml:835` | 1 | oft | Deutsches Beispiel bei deutscher Systemsprache. | hoch |
| P6 | Erinnerungen, Ergebnis | Funktioniert; Zeile "Laundry · in 20 min". | `after/expanded-reminders.png`, `reminder.png` | 1 | selten | Kurze Bestätigung. | mittel |
| P7 | Erinnerungen, Sichtbarkeit | Glocke nur wenn Erinnerungen an. | `MainWindow.Tools.cs:85` | 2 | selten | Hinweis auf Standardweg. | niedrig |
| P8 | Erster Eindruck (F1) | Offene Notch gut, Pause gross (46 px). | `after/expanded.png`; `MainWindow.xaml:635` | 1 | immer | Behalten. | hoch |

Noten (1-10)
- Verständlichkeit: 6
- Tempo: 7
- Vertrauen: 6
- Optik: 9
- Fehlertoleranz: 6
- Gesamt: 7

Nicht geprüft: Hover-Verhalten, Musiksteuerung live, Apply-Wirkung, Standardwert Erinnerungen.

Behalten: Die Notch öffnet sich schnell und sauber, und die Erinnerung "20 min laundry" versteht die App auf Anhieb.

# Persona 4 – Screenreader und Tastatur (Runde 2)

nicht live getestet

Grundbefund: Notch per Tastatur nicht bedienbar (kein Fokus, kein Hotkey, kein Tray). Ab dem Holm-Fenster weitgehend tastaturbedienbar.

| ID | Ort | Was passiert | Beleg | Schwere | Wie oft | Vorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P1 | Notch öffnen | Nicht per Tastatur; kein Hotkey, kein Tray-Icon. | `MainWindow.xaml:227-230`; `MainWindow.xaml.cs:1330-1344`; `ColorPicker.cs:109` | 5 | immer | Globaler Hotkey, Tray-Icon. | hoch |
| P2 | Holm-Fenster öffnen | Nur über Rechtsklick-Menü der Notch. | `MainWindow.xaml:231-245`; `MainWindow.Settings.cs:124-136` | 5 | immer | Hotkey/Tray. | hoch |
| P3 | Notch-Werkzeuge (F3) | Buttons `Focusable=False`, Feld erst nach Mausklick. | `MainWindow.xaml:6`, `:32`, `:175-183`; `MainWindow.Tools.cs:102-128`; `MainWindow.xaml.cs:197` | 5 | immer | Werkzeuge fokussierbar. | hoch |
| P4 | Holm-Einstellungen, Tab-Reihenfolge | Footer (Reset, Apply) vor den Schaltern in der Tab-Folge; Apply schwer erreichbar. | `SettingsWindow.cs:181-195`, `:223`, `:443` | 4 | oft | Footer nach Inhalt / TabIndex. | hoch |
| P5 | Schalter-Zustand | Name ohne ein/aus; kein AutomationPeer. | `SettingsWindow.cs:378`, `:410`, `:493-532`, `:455-462` | 4 | immer | Name mit Zustand oder Toggle-Peer. | hoch |
| P6 | Chat-Freundesliste | Nur Maus. | `ChatPage.cs:259`, `:412-419`, `:306-314` | 4 | oft | Fokussierbar, Pfeiltasten. | hoch |
| P7 | Chat, Esc | Esc schliesst Fenster, Entwurf verloren. | `SettingsWindow.cs:229-245`; `ChatPage.cs` | 4 | oft | Esc im Feld abfangen / Entwurf merken. | mittel |
| P8 | Eingabefelder ohne Namen | Reminder-Feld, Chat-Suche, Nachrichtenfeld ohne Name. | `MainWindow.xaml:833-836`; `HubTheme.cs:142-161`; `ChatPage.cs:131`, `:213` | 3 | immer | AutomationProperties.Name. | hoch |
| P9 | Chat-Aktionen | Add/Send files/GIF/Send als Border ohne Name. | `ChatPage.cs:144-153`, `:185-190`, `:232-239`, `:202-210` | 3 | oft | Keyboardable-Muster. | hoch |
| P10 | Rückmeldung Erinnerung | Hinweis nicht als Live-Region. | `MainWindow.xaml:838`; `MainWindow.Reminders.cs:78-88`, `:97` | 3 | oft | LiveSetting="Polite". | mittel |

Positiv: `SettingsWindow.Keyboardable` (Namen, Enter/Space, Fokusrahmen), Dialog-Fokus, Enter im Chat, Namen an Entfernen-Knöpfen.

## Noten (1-10)

- Verständlichkeit: 4
- Tempo: 2
- Vertrauen: 3
- Optik: 7
- Fehlertoleranz: 3
- Gesamt: 3

Behalten: Die `Keyboardable`-Hilfsfunktion in `SettingsWindow.cs` mit eigenem Fokusrahmen und Enter/Space – genau so sollte jeder andere Bereich von Holm gebaut werden.

# Persona 9 – Designer (Runde 2)

"nicht live getestet"

| ID | Ort | Was passiert | Beleg | Schwere | Wie oft | Vorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| D1 | Holm-Fenster, Navigation | Gruppen falsch sortiert (Weather unter SETTINGS, Wi-Fi allein unter TOOLS). | `after/settings.png` | 3 | immer | Neu gruppieren. | hoch |
| D2 | Kacheln und Schalter | Kachelfarben uneinheitlich; Schalter grün, Aktionen blau. | `settings.png`, `settings-popouts.png`; `HubTheme.cs:22`, `:23`, `:69` | 3 | immer | Ein Farbregelwerk. | hoch |
| D3 | Notch, Hub, Chat | Mehrere Akzentsysteme (Blau, Pink, Blau-Violett). | `expanded.png`; `hub-chat.png`; `HubTheme.cs:22` | 3 | immer | Ein Akzent pro Fläche. | mittel |
| D4 | Notch-Fussleiste | Gerät, Akku, Kopfhörer, Bluetooth, Lautstärke, oranges Icon gemischt. | `expanded.png`, `expanded-today.png` | 3 | immer | Geräte als eine Zeile; Icon beschriften. | mittel |
| D5 | Profil-Farbfelder | Keine sichtbare Auswahl im Screenshot. | `settings-profile.png`; `ProfileEditor.cs:169-176` | 3 | oft | Ring/Haken. | mittel |
| D6 | Notch-Kopfzeile | Sechs Icons ohne Text, Mond abgesetzt. | `expanded.png`; `MainWindow.xaml:537-553` | 2 | oft | Beschriftung/Gruppierung. | mittel |
| D7 | Heute-Ansicht | Vier konkurrierende Farben. | `expanded-today.png` | 2 | immer | Farbe nur für eine Sache. | mittel |
| D8 | Chat-Kopfzeile | "Back at 3" mehrdeutig (Demo-Daten). | `hub-chat.png`; `MainWindow.xaml.cs:2012`; `MainWindow.Hub.cs:111` | 2 | oft | Uhrzeit ausschreiben. | hoch |
| D9 | Chat-Badge | Sidebar "Chat 1", Liste ohne Badge. | `hub-chat.png`; `ChatPage.cs:396-402`, `:259` | 2 | selten | Gleiche Quelle. | niedrig |
| D10 | Radien/Abstände | Radien 7/8/10/12/14; viel Leerraum. | `HubTheme.cs:87`, `:167`; `SettingsWindow.cs:96`, `:150`, `:352`, `:371` | 1 | immer | Radien-Skala. | mittel |

## Noten (1-10)

- Verständlichkeit: 6
- Tempo: 7
- Vertrauen: 7
- Optik: 6
- Fehlertoleranz: 7
- Gesamt: 6

## Behalten

Die Apply-Logik mit ausgegrautem Apply ohne Änderungen, dem Dialog "Unapplied changes" beim Schliessen und der Bestätigung vor "Reset to defaults" unbedingt behalten.

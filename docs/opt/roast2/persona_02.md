# Persona 2 – Power User (Runde 2)

nicht live getestet

| ID | Ort | Was passiert | Beleg | Schwere | Wie oft | Vorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P1 | Zwischenablage (F6) | Einträge nur per Maus; keine Tastaturbedienung der Liste. | `MainWindow.xaml:183`; `MainWindow.Clipboard.cs:233`; `MainWindow.Tools.cs:33-39` | 5 | immer | Ziffern 1–9 / Pfeile + Enter. | hoch |
| P2 | Global (F2, F6, F9) | Keine Tastenkürzel; Notch nur per Maus. | `ColorPicker.cs:109-114`; `CapsLockWatcher.cs:47`, `:91`; `MainWindow.xaml.cs:1330`, `:1344` | 5 | immer | Belegbare globale Hotkeys. | hoch |
| P3 | Timer (F9) | Rechtsklick → Timer → 25 minutes; keine Tastatur. | `MainWindow.xaml:227`; `MainWindow.xaml.cs:1512-1515`, `:195-197`; `MainWindow.Timer.cs:152-177` | 4 | immer | Hotkey mit Dauer-Eingabe. | hoch |
| P4 | Timer, Dauer/Wechsel | Nur Presets; Umstellen 5 Aktionen. | `MainWindow.Timer.cs:18`, `:162-170` | 3 | oft | Freie Eingabe. | hoch |
| P5 | Zwischenablage, Reihenfolge | Zurückgeholter Eintrag springt nach oben. | `MainWindow.Clipboard.cs:67-72`; `ClipboardHistory.cs:40-49` | 3 | immer | Nicht umsortieren. | mittel |
| P6 | Zwischenablage, Seitenreset | Beim Zuklappen zurück auf Home. | `MainWindow.xaml.cs:428`; `MainWindow.Tools.cs:107-110` | 3 | oft | Letzte Seite merken. | hoch |
| P7 | Zwischenablage, Massenaktionen | Keine Mehrfachauswahl/Suche; Leeren ohne Rückfrage; max 20. | `ClipboardHistory.cs:31`; `MainWindow.Clipboard.cs:186-191`, `:263`, `:237-246` | 3 | oft | Rückfrage, Mehrfachauswahl, Anheften. | hoch |
| P8 | Notizen (F2) | Eine Notiz, 4000 Zeichen, kein Zähler. | `MainWindow.xaml:818-827`, `:822`; `Notepad.cs:8` | 2 | oft | Mehrere Notizen, Zähler. | hoch |
| P9 | Timer nach Neustart | Nur im Speicher. | `MainWindow.Timer.cs:21-23`, `:27-33` | 2 | selten | Endzeit sichern. | hoch |
| P10 | Timer stoppen | Kein Rückgängig. | `MainWindow.Timer.cs:168`; `MainWindow.xaml:793` | 2 | selten | Rückgängig 5 s. | hoch |

Noten:
Verständlichkeit: 6
Tempo: 2
Vertrauen: 6
Optik: 7
Fehlertoleranz: 4
Gesamt: 4

Behalten: Das Notizfeld fokussiert sich beim Öffnen sofort, und Esc speichert und schliesst in einem Schritt.

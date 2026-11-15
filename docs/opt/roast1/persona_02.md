# Persona 2 – Power User

nicht live getestet. Alle Punkte aus Code-Lesen und Screenshots. Code `WinNotch\`, Screenshots `docs\opt\before\`.

Kurzfazit: Für Tastatur-Nutzer gibt es praktisch keinen Weg. F2, F6 und F9 existieren nur mit der Maus, und die Listen sind auf Tempo nicht ausgelegt.

| ID | Ort | Was passiert | Beleg | Schwere (1-5) | Wie oft | Verbesserungsvorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P1 | Alle drei Abläufe, global | Kein Hotkey, um Notiz, Zwischenablage oder Timer zu öffnen. Notch nicht fokussierbar, kein Tab-/Pfeil-Weg. | `ColorPicker.cs:109`, `:113` (einziges RegisterHotKey); `MainWindow.xaml:32`; `MainWindow.xaml.cs:197`; `MainWindow.Tools.cs:9-13`, `:114-128` | 5 | immer | Konfigurierbare globale Hotkeys für Notiz, Zwischenablage, Timer. | hoch |
| P2 | Zwischenablage | Kein Tastaturweg: keine Suche, keine Zifferntasten 1–9, keine Mehrfachauswahl, kein Entfernen per Taste. | `MainWindow.xaml:878-884`, `MainWindow.Clipboard.cs:142-170`, `:201-206`; `MainWindow.Tools.cs:102-112`; `expanded-clipboard.png` | 4 | immer | Ziffern 1–9, Pfeile + Enter, Suchfeld. | hoch |
| P3 | Timer, Rechtsklick-Menü | 25-Minuten-Timer = 3 Mausklicks. Ohne Maus kein Weg. | `MainWindow.xaml:227-234`, `MainWindow.Timer.cs:152-177`; `expanded-timer.png`, `timer.png` | 4 | immer | Tastaturweg wie P1; "25m" eintippen startet. | hoch |
| P4 | Timer, Dauerliste | Feste Presets 1, 5, 10, 15, 25, 30, 45, 60. Keine freie Eingabe. | `MainWindow.Timer.cs:18`, `:172-176` | 3 | oft | Freie Eingabe oder "Zuletzt verwendet". | hoch |
| P5 | Zwischenablage, Klick | Klick kopiert, Eintrag springt nach oben, Liste rutscht unter dem Cursor. | `MainWindow.Clipboard.cs:142-170`, `:67-73`; `ClipboardHistory.cs:40-49`; `MainWindow.xaml:883` | 3 | oft | Eintrag an Position lassen oder Nummern zeigen. | mittel |
| P6 | Notiz | Genau eine Notiz, 4000 Zeichen, still gekürzt, kein Zähler. | `MainWindow.Notes.cs:22`, `:28`, `:48`; `Notepad.cs:8`, `:10`; `expanded-notes.png` | 3 | oft | Zähler ab ~3500, mehrere Notizen. | mittel |
| P7 | Zwischenablage, Papierkorb | "Clear the list" leert sofort, ohne Bestätigung/Rückgängig. | `MainWindow.xaml:873-874`, `MainWindow.Clipboard.cs:186-191`, `:263` | 3 | selten | Bestätigung oder Rückgängig-Hinweis. | hoch |
| P8 | Timer neu starten | Läuft ein Timer, verschwinden die Presets; erst Stop, dann neu. | `MainWindow.Timer.cs:162-169` | 2 | oft | "Neu starten mit …" im laufenden Menü. | hoch |
| P9 | Notiz, Esc | Esc schliesst die ganze Notch statt nur das Feld zu verlassen. | `MainWindow.Tools.cs:105-111` | 2 | oft | Esc in zwei Stufen. | mittel |
| P10 | Zwischenablage, Scrollbereich | MaxHeight 236, Scrollbar versteckt; bei 20 Einträgen kein Hinweis auf mehr. | `MainWindow.xaml:880`; `expanded-clipboard.png` | 2 | oft | Dezente Scrollbar oder "1–5 von 20". | mittel |

Noten:
Verständlichkeit: 6
Tempo: 3
Vertrauen: 7
Optik: 8
Fehlertoleranz: 4
Gesamt: 4

Behalten: Der sofortige Schreibfokus beim Klick auf das Notiz-Icon (`MainWindow.Tools.cs:29-30`) mit automatischem Speichern beim Schliessen ist die einzige Stelle, an der diese App schon wie ein Werkzeug für Leute mit Tempo wirkt.

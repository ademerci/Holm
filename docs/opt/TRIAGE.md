# TRIAGE – Roast Runde 1

Priorität = Schwere × Personas / Aufwand (S=1, M=2, L=4). Status: bestätigt (Code-Stelle gelesen), Geschmack, verworfen.
Runde-1-Noten (Gesamt): P1 5 · P2 4 · P3 5 · P4 3 · P5 5 · P6 4 · P7 5 · P8 7 · P9 6 · P10 5 → **Ø 4,9**.

## Gewählt (umgesetzt in Phase 8)

| # | Problem | Personas | Schwere | Aufwand | Prio | Status | Umsetzung |
|---|---|---|---|---|---|---|---|
| T1 | Icon-Buttons ohne Namen; Prev/Play/Next ohne Tooltip | 1,3,4,5,8,9 | 4 | S | 24 | bestätigt (MainWindow.xaml:632-638, 0× AutomationProperties) | Tooltips + AutomationProperties.Name an 21 Elementen |
| T2 | Einstellungsschalter/Navigation nicht per Tastatur, keine Namen | 4 | 5 | S | 5 | bestätigt (SettingsWindow.cs Row/NavItem nur MouseLeftButtonUp) | Fokus, Space/Enter, Namen, Fokus-Rahmen |
| T3 | Doppelklick auf × löscht zwei Erinnerungen | 6,4 | 4 | S | 8 | bestätigt (MainWindow.Reminders.cs:99-126) | 400-ms-Sperre nach Entfernen, Name am × |
| T4 | Unlesbare Erinnerung: Enter tut nichts, kein Grund | 1,4,6 | 2–3 | S | 8 | bestätigt (MainWindow.Reminders.cs:94) | Hinweis "Couldn’t find a time – try …" |
| T5 | Wetter-Suche: doppeltes Enter, Esc speichert still weiter | 6,10 | 3–4 | S | 7 | bestätigt (MainWindow.Weather.cs:176-197) | Laufende Suche sperrt, Speichern nur wenn noch gewählt wird, "(Esc to cancel)" |
| T6 | "Weather unavailable · choose city" trotz gespeichertem Ort | 6,10 | 2–3 | S | 6 | bestätigt (MainWindow.Weather.cs:123-125) | "Can't reach the weather service · trying again soon" |
| T7 | Speedtest-Ping ohne Zeitlimit | 3,10 | 3–4 | S | 7 | bestätigt (SpeedTest.cs:49, 75-84) | 5 s pro Ping, klare Fehlermeldung |
| T8 | Beam: "X is offline", obwohl eigenes Netz schuld sein kann | 3 | 5 | S | 5 | bestätigt (MainWindow.Share.cs:496-512) | "Couldn't reach X right now / It goes once you're both online" |
| T9 | Rohe IOException-Texte; Accept→Cancel-Doppelklick | 3,6 | 3 | S | 6 | bestätigt (MainWindow.Share.cs:800-807, 895) | Klartext, 600-ms-Sperre für Cancel |
| T10 | Einladungscode: wer ihn hat, wird Freund – nicht gesagt; Server unbenannt | 7 | 4 | S | 4 | bestätigt (AddFriendPage.cs:70, 152, 160) | Klarer Satz, Server im Fehler genannt |
| T11 | Wetter-Ortung ohne Beschreibung in Settings; Clipboard-Datenschutz nicht erklärt | 7 | 3–4 | S | 4 | bestätigt (MainWindow.Settings.cs:67-70) | Beschreibungen ergänzt |

(Vor Runde 1 bereits umgesetzt, Phase 4: Autofocus Notiz/Erinnerung/Rechner/Wetter, Enter = Apply im Profil.)

## Verworfen (je ein Satz)

| Problem | Grund |
|---|---|
| Globale Hotkeys (P2, P4) | Neue Funktion (L), kollidiert mit "nimmt nie ungefragt Fokus"; Backlog. |
| Tray-Icon (P4) | Neue Funktion (L); Backlog. |
| Beschriftung unter jedem Toolbar-Icon (P1, P5, P8, P9) | Layout-Umbau der Notch (L); Tooltips + Namen als Teil-Lösung (T1). |
| Fällige Erinnerung geht bei Klick ausserhalb/Timeout verloren (P1, P6, Schwere 5) | Verhaltensänderung (erneut klingeln?) braucht Produktentscheid; Backlog mit hoher Priorität. |
| Akzentfarben vereinheitlichen, Icons/Avatare, Datumsformat (P9) | Geschmack bzw. Design-System-Entscheid; Regel Phase 8.3. |
| Grössere Slider-Trefferfläche (P3, P5) | Sinnvoll, aber Custom-Control-Änderung (M) mit Layout-Risiko; Backlog. |
| Notch-Grösse in Holm-Einstellungen (P5) | M, Backlog; Weg über Rechtsklick existiert. |
| QR für Beam-Code, Drag aus Screenshot, Markup (P8) | Neue Funktionen; Backlog. |
| Equalizer standardmässig aus (P10) | Kernmerkmal; Schalter existiert. |
| Beam-Heartbeat/Restzeit (P3), Abbruch-Frames (P6) | Protokolländerung = öffentliche Schnittstelle (Regel 10). |
| AllowFallbackToConsentlessPositions (P7) | Unklar, ob Verhalten abweicht; nicht geprüft → Backlog. |
| Netz-Absender als "unbestätigt" (P7) | M; Backlog. |
| Mehrere Notizen, Timer frei, Clipboard-Ziffern (P2) | Neue Funktionen; Backlog. |
| Clipboard "Clear" ohne Bestätigung (P2) | Liste ist flüchtig (nur Speicher); Geschmack. |

## Widersprüche

- Power User will dichter/schneller, Senior will grösser: Lösung für beide = Tastatur-/Screenreader-Namen (T1, T2) und
  bestehende Grössen-Einstellung; keine Verdichtung.

Advisor-Frage zu Triage: auf Wunsch des Nutzers (Tempo, 15–30 min) übersprungen; Advisor vor Abschluss bleibt.

# Persona 5 – Senior, schlechte Sehkraft

"nicht live getestet"

Geprüft wurden Screenshots (idle, compact, expanded, expanded-calculator, volume, settings) und der Code. Keine Klicks, keine Live-Bedienung. Trefferflächen und Kontrast aus Code und Bild abgeleitet, nicht gemessen.

| ID | Ort | Was passiert | Beleg | Schwere (1-5) | Wie oft | Verbesserungsvorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P1 | Expanded, Lautstärke-Slider (F1) | Slider 84 px breit, nur 16 px hoch. Spur 4 px, erst bei Hover 7 px. Ein Senior trifft die Spur nur mit Mühe. | `WinNotch\Controls.cs:76-84`, `WinNotch\MainWindow.xaml:915`, `expanded.png` | 5 | oft | Trefferhöhe mindestens 32 px. Tasten "−"/"+". Spur dauerhaft 6–7 px. | hoch |
| P2 | Expanded, Icon-Leiste oben (F2, F3, F5, F6, F7, Pipette) | Sechs Icons ohne Text. Lautsprecher-Button öffnet "Volume per app", sieht aus wie System-Lautstärke. Namen nur als Tooltip. | `WinNotch\MainWindow.xaml:537-553`, `expanded.png`, `expanded-mixer.png` | 5 | immer | Unter jedem Icon ein Wort. Eigenes Mixer-Symbol. | hoch |
| P3 | Idle und überall | Schrift 10–11 px bei Normal-Grösse. Idle-Uhr "18:03" winzig und grau. | `WinNotch\MainWindow.xaml:347`, `:351`, `:364`, `:534`, `:611`, `:621`, `:857`; `idle.png` | 4 | immer | Mindestschrift 13 px für Informationen. | hoch |
| P4 | Überall, Sekundärtext | "Dim" 65 % Weiss, "Faint" 40 %. Faint trägt Spotify-Namen. Kontrast grob ~3:1. | `WinNotch\MainWindow.xaml:13`, `:14`, `:611`, `:676` | 3 | oft | Faint nicht für Text, Dim ≥ 80 %. | mittel |
| P5 | Rechner (F5), Ergebnis kopieren | "Click or Enter to copy" und "57.5 copied" in 11 px – Feedback in der kleinsten Schrift. | `WinNotch\MainWindow.xaml:853`, `:857`, `WinNotch\MainWindow.Calculator.cs:24`, `:39`; `expanded-calculator.png` | 3 | oft | Häkchen/Farbwechsel am Ergebnis. Hinweis 13 px. | mittel |
| P6 | Rechner, Eingabe | Ob das Feld beim Öffnen Fokus bekommt, nicht geprüft. | `expanded-calculator.png` | 2 | oft | Fokus beim Öffnen ins Feld. | niedrig |
| P7 | Lautstärke-Pop-out | "62 %" nur Anzeige, nicht bedienbar. | `WinNotch\MainWindow.xaml:443`, `volume.png` | 2 | oft | Pop-out bei Mausübern bedienbar oder Hinweis "Scroll to change". | hoch |
| P8 | Notch-Grösse (F15) | Grösse nicht in Holm-Einstellungen, nur Rechtsklick → Customize → Collapsed/Expanded size. "Expanded" unerklärt. Wirkt sofort statt nach Apply. Extra large nur 1.3. | `WinNotch\MainWindow.xaml:246-256`, `WinNotch\MainWindow.xaml.cs:27`, `:587-591`, `WinNotch\MainWindow.Settings.cs:26`; `settings.png` | 3 | selten | Grösse in Holm-Einstellungen mit Vorschau und Satz "Makes everything in the notch bigger". | hoch |
| P9 | Holm-Einstellungen (F15) | Gut lesbar. Apply im Ruhezustand grau und schwach. | `settings.png`, `WinNotch\SettingsWindow.cs:230-243`, `:183` | 1 | selten | Apply im Ruhezustand kräftiger. | mittel |

Noten (1–10):
- Verständlichkeit: 5
- Tempo: 6
- Vertrauen: 6
- Optik: 7
- Fehlertoleranz: 5
- Gesamt: 5

Behalten: Der Rechner mit grossem Ergebnisblock und Klick zum Kopieren zeigt die Antwort sofort und ohne Umweg.

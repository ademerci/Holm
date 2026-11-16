# Persona 5 – Senior, schlechte Sehkraft (Runde 2)

nicht live getestet. Kontrastwerte geschätzt.

| ID | Ort | Was passiert | Beleg | Schwere | Wie oft | Vorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P1 | F1 Lautstärke | Spur 4 px, Trefferfläche 16 px, keine Prozentzahl, keine +/-. | `after/expanded.png`; `Controls.cs:76-77`, `:84` | 5 | oft | Prozent, +/-, Spur ≥ 8 px, Treffer ≥ 24 px. | hoch |
| P2 | Notch-Leiste oben | Icons ohne Text; zwei Lautsprecher mit verschiedener Bedeutung. | `after/expanded.png`, `expanded-mixer.png` | 4 | oft | Textlabels; unterscheidbare Symbole. | hoch |
| P3 | F5 Rechner-Hinweis | "Click or Enter to copy" 11 px Dim. | `after/expanded-calculator.png`; `MainWindow.xaml:857` | 4 | oft | ≥ 13 px, Kopier-Symbol. | mittel |
| P4 | F15 Rechtsklick | Ruhe-Notch 150×30 px; Grösse nur unter Customize, nicht in Settings. | `MainWindow.xaml:227`, `:246-256`; `MainWindow.Settings.cs:26` | 4 | selten | Grösse in Settings mit Vorschau. | hoch |
| P5 | F5 Kopierbestätigung | Nur 11-px-Text "57.5 copied". | `MainWindow.Calculator.cs:39`; `MainWindow.xaml:857` | 3 | oft | Grössere Bestätigung/Farbwechsel. | hoch |
| P6 | F15 Settings-Texte | Beschreibungen 12–13 px (Kontrast ok ~6,8:1, Grösse zu klein). | `after/settings.png`; `SettingsWindow.cs:162`, `:400`; `HubTheme.cs:20` | 3 | immer | ≥ 14 px. | mittel |
| P7 | Mixer stumm | 35 % Deckkraft, "Muted" 11 px, Mute-Button nur Icon. | `after/expanded-mixer.png`; `MainWindow.Mixer.cs:120`, `:92`, `:84` | 3 | oft | Text "Muted"/"Mute". | hoch |
| P8 | Platzhalter | 13 px Faint (~3,8:1). | `HubTheme.cs:21`, `:153` | 2 | oft | Auf Dim anheben. | mittel |
| P9 | F15 Apply | Apply bei unverändert 40 % Deckkraft; Dialog bei Esc gut. | `after/settings.png`; `HubTheme.cs:102`; `SettingsWindow.cs:231-244` | 2 | selten | Hinweiszeile. | mittel |

Noten (1-10):
- Verständlichkeit: 5
- Tempo: 6
- Vertrauen: 6
- Optik: 7
- Fehlertoleranz: 5
- Gesamt: 5

Nicht geprüft: Hover-Reaktion, reale Pixel, Extra-large-Skalierung, Kontrast auf echtem Verlauf, Screenreader.

Behalten: Das Lautstärke-Popup (62 % gut lesbar) und das grosse Ergebnisfeld im Rechner zeigen, dass grosse Zielflächen funktionieren – diese Idee auf die übrigen Bedienelemente übertragen.

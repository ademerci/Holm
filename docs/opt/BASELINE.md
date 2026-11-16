# BASELINE – Holm vor der Optimierung

Stand: Commit `2a92abd` (= `opt/start` + dev-only `HOLM_DATA`), 2026-11-15, gleicher PC wie alle späteren Messungen.
Methode: `docs/opt/measure.ps1` (je 3 Läufe, Median, Warm-up-Lauf zählt nicht), `docs/opt/ab-start.ps1` (A/B
abwechselnd, 7 Paare), `--bench` (MainWindow.Bench.cs). Rohdaten: `measure-before.json`, `measure-before-live.json`.
Live-Läufe: Bench-Build (= Release + Bench-Schalter), Testdaten, offline (DECISIONS.md 4–6), neben dem eigenen Holm.

**Rauschen:** Der PC wird parallel genutzt, Musik lief zeitweise (Equalizer). Gleiche Exe, zwei Durchgänge: Start
858 ms vs 1821 ms, Idle-CPU 3188 vs 1583 ms/min. Darum: Grösse = exakt; Start/CPU = nur A/B-Vergleiche zählen.

## 1. Grösse

| Wert | Vorher | Quelle |
|---|---|---|
| Holm.exe (Release, self-contained single-file) | **212,96 MB** | measure-before.json |
| Release-Zip (exe + LICENSE + README) | **82,04 MB** | measure-before.json |
| davon Composite-R2R-Image `Holm.r2r.dll` | 122,04 MB | Bundle-Extraktion (DOTNET_BUNDLE_EXTRACT_BASE_DIR) |
| Ordner-Publish (nicht single-file) | 163,3 MB, 262 Dateien | `out/m-dir` |
| Direkte NuGet-Abhängigkeiten | 2 (QRCoder, Emoji.Wpf) + transitiv Typography.OpenFont/GlyphLayout | csproj |
| node_modules / Bundle pro Route | nicht anwendbar (keine Web-App) | – |
| Assets (Bilder, Schriften, Sound) | 385,1 KB (notch.ico 381 KB, incoming-file.wav 11,6 KB), keine Schriften | – |
| Quellcode | 129 Dateien (.cs/.xaml), 26 428 Zeilen | measure-before.json |

## 2. Geschwindigkeit

| Wert | Vorher | Quelle |
|---|---|---|
| Build Release (clean, nur App) | 3,55 s | 3 Läufe |
| Publish Release (clean, R2R single-file) | 67,2 s | 3 Läufe |
| Testlauf (1020 Tests) | 52,2 s | 3 Läufe |
| Start bis erstes sichtbares Fenster | **2188 ms** (A/B-Median, 7 Läufe); Durchgänge: 858 / 1821 ms | ab-start, measure |
| Start bis bedienbar (WaitForInputIdle) | = Fenster sichtbar (1821 ms); Methode trennt nicht → nicht messbar getrennt | measure-before-live |
| CPU in den ersten 10 s | **6375 ms** (A/B-Median) | ab-start |
| CPU in den ersten 25 s | 6469 / 6625 ms | measure |
| Notch öffnen: Aufruf / längster Frame | 23,5 ms / 33,9 ms | --bench quick |
| CPU pro Öffnen+Schliessen | 653 / 822 ms (zwei Durchgänge) | --bench quick |
| Übersprungene Frames (10 Runden) | open 31 / close 36 (Durchgang 2: 87 / 44) | --bench quick |
| Lighthouse Performance/Accessibility | nicht anwendbar (kein Browser) | – |
| API-Antwortzeiten | nicht anwendbar (keine eigene API; externe Dienste offline geschaltet) | – |
| Netzwerkanfragen beim Start (Testkonfig) | 0 extern (Update-Check, Ortung, Beam aus) | Seed-Settings |
| DB-Abfragen pro Ablauf | nicht anwendbar (keine DB; JSON-Dateien) | – |

## 3. Prozesse (aus FLOWS.md, gezählt im Code)

| Ablauf | Klicks | Felder | Wartezeiten |
|---|---|---|---|
| F2 Notiz | 2 + Klick ins Feld = 3 | 1 | 0 |
| F3 Erinnerung | 2 + Klick ins Feld = 3, Enter | 1 | 0 |
| F5 Rechnen | 2 + Klick ins Feld + Klick Ergebnis = 4 | 1 | 0 |
| F15 Einstellung ändern | 5 (Rechtsklick, Settings…, Seite, Schalter, Apply) | 0 | 0 |
| F16 Wetter-Ort | 3 + Klick ins Feld = 4 | 1 | Geocoding |
| F19 Speedtest | 3 | 0 | ~15 s |
| F21 Profil | 3–5, kein Enter | 2 | 0 |
| übrige | siehe FLOWS.md | | |

## 4. Ressourcen (Idle, Notch zu, Testdaten)

| Wert | Vorher (Durchgang 1 / 2) |
|---|---|
| Private Memory | 153,7 / 149,0 MB (A/B nach 10 s: 142,8 MB) |
| Working Set | 87,1 / 73,9 MB |
| Peak Working Set | 241,3 / 240,7 MB |
| Idle-CPU | 3188 / 1583 ms/min – hängt an Musik: Profil zeigt Equalizer-Timer 452 ms/min UI-Thread |
| Threads / Handles | 66 / 1198 |
| Holm window 5× öffnen und schliessen | Private +56 MB, bleibt (GC-Heap nur +1,8 MB → nativ) |

Idle-Profil (UI-Thread, 1 min, Musik an): Equalizer 452 ms, WatchFullscreen 90 ms (Artefakt: zwei Holm-Notches
verdecken sich gegenseitig und ordnen sich neu), Messages-Poll 39 ms, Rest < 5 ms.

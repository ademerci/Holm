# PLAN – Optimierung in Wellen

Grundlage: 4 Analysen (`analysis/`), eigene Messungen (A/B abwechselnd, `ab-start.ps1`), Start-Trace
(`trace-patch.py`, nie committet). Gestrichen = keine messbare Wirkung oder Messung widerspricht.

## Geprüft und gestrichen

| Fund | Warum gestrichen | Beleg |
|---|---|---|
| `PublishReadyToRunComposite=false` (−56 MB) | Start bis bedienbar +703 ms (+24 %), CPU +1062 ms, JIT 636 → 1242 Methoden | A/B 15 Paare, `out/ab-composite.txt` |
| `PublishReadyToRunCompositeRoots` | SDK-Task stürzt ab (ArgumentOutOfRange) / NETSDK1152 – 3× gescheitert, Regel 12 | Publish-Logs |
| Clipboard-Thumbnails ohne DecodePixelWidth | falsch: Thumb wird auf 72 px skaliert | MainWindow.Clipboard.cs:123-125 |
| WatchFullscreen 2,2 ms/Tick | Messartefakt: zwei Holm-Notches verdecken sich, SetWindowPos jede 1,5 s | Native.IsCovered, Profil |
| Messages-Poll 2 s | 39 ms/min = 0,07 % CPU | Idle-Profil |
| Equalizer-Tick | schon schlank (4 ScaleY); fps senken = sichtbare Änderung | MainWindow.xaml.cs:750 |
| Settings.Save debouncen / atomar | keine Messwirkung im Ablauf; Robustheit → Backlog | – |
| WinForms im Bundle | nicht enthalten | Ordner-Publish |
| Theme-DLLs entfernen | Classic = High-Contrast-Fallback (Accessibility) | – |
| QRCoder ersetzen | < 1 MB, Risiko Scan-Kompatibilität | – |
| Speedtest Autostart | Datenverbrauch ohne Zustimmung | NetworkPage.cs:56 |
| Apply-Semantik entfernen | Verhaltensänderung, Regel 9 | – |

## Welle 1 – kein Risiko

1. **Composite-Ausschlüsse**: 44 grosse Assemblies, die beim Start nicht geladen werden, behalten Microsofts eigenen
   vorkompilierten Code statt voll ins Composite. Gemessen 213,15 → 204,38 MB, JIT bis ready gleich (634). A/B läuft.
2. **Icon-Duplikat**: 30-px-Markenbild aus 128-px-PNG statt ganzer 381-KB-.ico als WPF-Resource (Advisor: 128 statt 64 px
   für Schärfe bei hoher Skalierung). Erwartet −0,37 MB.

## Welle 2 – mittleres Risiko

3. **EcoQoS nach dem Start**: Effizienzmodus erst bei ApplicationIdle statt in SourceInitialized. Trace: 1,5 s des
   Starts laufen gedrosselt. Messen per A/B (bedienbar-Zeit, CPU). Idle-Verhalten unverändert.
4. **Autofocus Tool-Seiten** (F2 Notiz, F3 Erinnerung, F5 Rechner): Klick auf Tool-Icon = Absicht zu tippen → Feld
   nimmt Tastatur (der Klick ging an Holm, Windows erlaubt Vordergrund). Bedingungen (Advisor): Maus weg ohne Tippen →
   Notch schliesst trotzdem; Esc gibt Fokus zurück. In-Process-Test. DECISIONS-Eintrag. −1 Klick je Ablauf.
5. **Wetter-Ort** (F16): gleiches Prinzip für das Ortsfeld. −1 Klick.
6. **Profil Enter = Apply** (F21), Default-Button im „nicht angewendet“-Dialog.

## Welle 3 – nur wenn Ziele offen

7. Holm window +56 MB nach Schliessen: erst `HOLM_BENCH_HUB` 5 vs 20 Runden (Einmal-Ladung oder Leck?).
8. Startpfad: StartSharing/StartPrivacy/Media (zusammen ~460 ms in OnLoaded) auf ApplicationIdle verschieben, nur
   mit A/B-Beleg und ohne Reihenfolge-Abhängigkeiten.

Nach jeder Welle: `gate.ps1`, Messung, STATE.md.
